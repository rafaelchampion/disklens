using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using DiskTree.Core.Classify;
using DiskTree.Core.Tree;

namespace DiskTree.Core.Scan;

public static class DirectoryScanner
{
    private const uint FILE_ATTRIBUTE_HIDDEN = 0x00000002;
    private const uint FILE_ATTRIBUTE_SYSTEM = 0x00000004;
    private const uint FILE_ATTRIBUTE_DIRECTORY = 0x00000010;
    private const uint FILE_ATTRIBUTE_REPARSE_POINT = 0x00000400;
    private const uint FILE_ATTRIBUTE_SPARSE_FILE = 0x00000200;
    private const uint FILE_ATTRIBUTE_COMPRESSED = 0x00000800;

    private const uint IO_REPARSE_TAG_MOUNT_POINT = 0xA0000003;
    private const uint IO_REPARSE_TAG_SYMLINK = 0xA000000C;

    private const int FIND_FIRST_EX_LARGE_FETCH = 2;

    #region Win32 Interop — FindFirstFileEx

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WIN32_FIND_DATAW
    {
        public uint dwFileAttributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME ftCreationTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME ftLastAccessTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME ftLastWriteTime;
        public uint nFileSizeHigh;
        public uint nFileSizeLow;
        public uint dwReserved0;
        public uint dwReserved1;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string cFileName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 14)]
        public string cAlternateFileName;
    }

    private enum FINDEX_INFO_LEVELS
    {
        FindExInfoStandard = 0,
        FindExInfoBasic = 1,
        FindExInfoMaxInfoLevel
    }

    private enum FINDEX_SEARCH_OPS
    {
        FindExSearchNameMatch = 0,
        FindExSearchLimitToDirectories = 1,
        FindExSearchLimitToDevices = 2,
        FindExSearchMaxSearchOp
    }

    [DllImport("kernel32.dll", EntryPoint = "FindFirstFileExW",
        SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr FindFirstFileEx(
        string lpFileName,
        FINDEX_INFO_LEVELS fInfoLevelId,
        out WIN32_FIND_DATAW lpFindFileData,
        FINDEX_SEARCH_OPS fSearchOp,
        IntPtr lpSearchFilter,
        uint dwAdditionalFlags);

    [DllImport("kernel32.dll", EntryPoint = "FindNextFileW",
        SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FindNextFile(
        IntPtr hFindFile, out WIN32_FIND_DATAW lpFindFileData);

    [DllImport("kernel32.dll", EntryPoint = "FindClose",
        SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FindClose(IntPtr hFindFile);

    private static readonly IntPtr INVALID_HANDLE_VALUE = new(-1);

    #endregion

    #region Win32 Interop — MFT / USN Journal

    [DllImport("kernel32.dll", SetLastError = true,
        CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateFileW(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        IntPtr lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(
        IntPtr hDevice,
        uint dwIoControlCode,
        IntPtr lpInBuffer,
        uint nInBufferSize,
        IntPtr lpOutBuffer,
        uint nOutBufferSize,
        out uint lpBytesReturned,
        IntPtr lpOverlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr hObject);

    private const uint GENERIC_READ = 0x80000000;
    private const uint FILE_SHARE_READ = 0x00000001;
    private const uint FILE_SHARE_WRITE = 0x00000002;
    private const uint OPEN_EXISTING = 3;
    private const uint FILE_ATTRIBUTE_NORMAL = 0x80;

    // FSCTL_ENUM_USN_DATA for MFT enumeration
    private const uint FSCTL_ENUM_USN_DATA = 0x000900B3;
    // FSCTL_GET_NTFS_VOLUME_DATA to get cluster size
    private const uint FSCTL_GET_NTFS_VOLUME_DATA = 0x00090064;

    [StructLayout(LayoutKind.Sequential)]
    private struct MFT_ENUM_DATA_V0
    {
        public ulong StartFileReferenceNumber;
        public long LowUsn;
        public long HighUsn;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct USN_RECORD_V2
    {
        public uint RecordLength;
        public ushort MajorVersion;
        public ushort MinorVersion;
        public ulong FileReferenceNumber;
        public ulong ParentFileReferenceNumber;
        public long Usn;
        public long TimeStamp;
        public uint Reason;
        public uint SourceInfo;
        public uint SecurityId;
        public uint FileAttributes;
        public ushort FileNameLength;
        public ushort FileNameOffset;
    }

    // First three fields of NTFS_VOLUME_DATA_BUFFER we need
    [StructLayout(LayoutKind.Sequential)]
    private struct NTFS_VOLUME_DATA_BUFFER
    {
        public long VolumeSerialNumber;
        public long NumberSectors;
        public long TotalClusters;
        public long FreeClusters;
        public long TotalReserved;
        public uint BytesPerSector;
        public uint BytesPerCluster;
        public uint BytesPerFileRecordSegment;
        public uint ClustersPerFileRecordSegment;
        public long MftValidDataLength;
        public long MftStartLcn;
        public long Mft2StartLcn;
        public long MftZoneStart;
        public long MftZoneEnd;
    }

    #endregion

    /// <summary>
    /// Scan a directory tree. Tries MFT fast path for NTFS drive
    /// roots, falls back to FindFirstFileEx work-queue scan.
    /// </summary>
    public static async Task<Node> ScanAsync(
        string rootPath,
        ScanOptions options,
        ScanProgress progress,
        CancellationToken cancellationToken = default)
    {
        string normalizedRoot = Path.GetFullPath(rootPath)
            .TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);
        string rootName = Path.GetFileName(normalizedRoot);
        if (string.IsNullOrEmpty(rootName))
            rootName = normalizedRoot;

        // Try MFT scan for whole-drive NTFS roots (WizTree-style).
        // This reads every file record on the volume in one pass.
        if (IsDriveRoot(normalizedRoot))
        {
            var mftResult = await Task.Run(
                () => TryMftScan(
                    normalizedRoot, rootName, options, progress,
                    cancellationToken),
                cancellationToken);
            if (mftResult != null)
            {
                progress.Finish();
                TreeAggregation.Aggregate(mftResult, options.Metric);
                Classifier.Classify(mftResult);
                return mftResult;
            }
        }

        // Fallback: work-queue based FindFirstFileEx scan.
        // Uses a Channel<WorkItem> so workers never block while
        // waiting for children — no deadlock possible.
        var rootNode = Node.CreateDirectory(rootName);
        int workerCount = Math.Clamp(
            Environment.ProcessorCount * 2, 4, 64);

        await RunWorkQueueScan(
            normalizedRoot, rootNode, workerCount, options,
            progress, cancellationToken);

        progress.Finish();
        TreeAggregation.Aggregate(rootNode, options.Metric);
        Classifier.Classify(rootNode);
        return rootNode;
    }

    #region Work-Queue Scanner (deadlock-free)

    private readonly record struct WorkItem(
        string Path, Node DirNode, int Depth);

    /// <summary>
    /// Fixed pool of workers pulling from an unbounded channel.
    /// Each worker enumerates one directory per iteration: it reads
    /// its entries, adds child nodes, and pushes subdirectories back
    /// into the channel. No worker ever waits for children, so the
    /// tree depth cannot cause a deadlock.
    /// </summary>
    private static async Task RunWorkQueueScan(
        string rootPath,
        Node rootNode,
        int workerCount,
        ScanOptions options,
        ScanProgress progress,
        CancellationToken ct)
    {
        var channel = Channel.CreateUnbounded<WorkItem>(
            new UnboundedChannelOptions { SingleWriter = false });

        await channel.Writer.WriteAsync(
            new WorkItem(rootPath, rootNode, 0), ct);

        // Track in-flight items. Starts at 1 for the root.
        int pending = 1;

        var workers = new Task[workerCount];
        for (int i = 0; i < workerCount; i++)
        {
            workers[i] = Task.Run(async () =>
            {
                await foreach (var item in
                    channel.Reader.ReadAllAsync(ct))
                {
                    if (progress.IsCancelled
                        || ct.IsCancellationRequested)
                    {
                        if (Interlocked.Decrement(ref pending) == 0)
                            channel.Writer.TryComplete();
                        continue;
                    }

                    int newDirs = EnumerateDirectory(
                        item.Path, item.DirNode, item.Depth,
                        options, progress, channel.Writer, ct);

                    if (newDirs > 0)
                        Interlocked.Add(ref pending, newDirs);

                    if (Interlocked.Decrement(ref pending) == 0)
                        channel.Writer.TryComplete();
                }
            }, ct);
        }

        await Task.WhenAll(workers);
    }

    /// <summary>
    /// Enumerate one directory: files go straight into the node's
    /// children, subdirectories get pushed into the work channel.
    /// Returns the number of subdirectory items enqueued.
    ///
    /// Each directory is processed by exactly one worker, so the
    /// Children list needs no locking.
    /// </summary>
    private static int EnumerateDirectory(
        string dirPath,
        Node dirNode,
        int currentDepth,
        ScanOptions options,
        ScanProgress progress,
        ChannelWriter<WorkItem> writer,
        CancellationToken ct)
    {
        if (options.MaxDepth.HasValue
            && currentDepth >= options.MaxDepth.Value)
            return 0;

        progress.CountDir();

        // Use \\?\ prefix for long-path support. Build the
        // search pattern once — no per-file prefix allocation.
        string searchDir = dirPath.StartsWith(@"\\?\")
            ? dirPath
            : @"\\?\" + dirPath;
        string searchPattern = searchDir.EndsWith(
                Path.DirectorySeparatorChar)
            ? searchDir + "*"
            : searchDir + Path.DirectorySeparatorChar + "*";

        IntPtr handle = FindFirstFileEx(
            searchPattern,
            FINDEX_INFO_LEVELS.FindExInfoBasic,
            out WIN32_FIND_DATAW findData,
            FINDEX_SEARCH_OPS.FindExSearchNameMatch,
            IntPtr.Zero,
            FIND_FIRST_EX_LARGE_FETCH);

        if (handle == INVALID_HANDLE_VALUE)
        {
            int err = Marshal.GetLastWin32Error();
            if (err is not 2 and not 18)
            {
                dirNode.ReadError = true;
                progress.RecordError(
                    dirPath, $"Win32 error {err}");
            }
            return 0;
        }

        int enqueued = 0;

        try
        {
            do
            {
                if (progress.IsCancelled
                    || ct.IsCancellationRequested)
                    break;

                string name = findData.cFileName;
                if (name is "." or "..")
                    continue;

                uint attrs = findData.dwFileAttributes;
                bool isHidden =
                    (attrs & (FILE_ATTRIBUTE_HIDDEN
                        | FILE_ATTRIBUTE_SYSTEM)) != 0;
                if (isHidden && !options.IncludeHidden)
                    continue;

                bool isDir =
                    (attrs & FILE_ATTRIBUTE_DIRECTORY) != 0;
                bool isReparse =
                    (attrs & FILE_ATTRIBUTE_REPARSE_POINT) != 0;

                string fullPath = Path.Combine(dirPath, name);
                long modSecs = ToUnixSeconds(
                    findData.ftLastWriteTime);

                if (isDir)
                {
                    if (isReparse && !options.FollowLinks)
                    {
                        uint tag = findData.dwReserved0;
                        if (tag is IO_REPARSE_TAG_MOUNT_POINT
                            or IO_REPARSE_TAG_SYMLINK)
                        {
                            dirNode.Children.Add(
                                Node.CreateEntry(
                                    name, NodeKind.Symlink,
                                    0, modSecs));
                            continue;
                        }
                    }

                    var childDir =
                        Node.CreateDirectory(name);
                    childDir.Modified = modSecs;
                    dirNode.Children.Add(childDir);

                    writer.TryWrite(new WorkItem(
                        fullPath, childDir,
                        currentDepth + 1));
                    enqueued++;
                }
                else
                {
                    ulong apparent =
                        ((ulong)findData.nFileSizeHigh << 32)
                        | findData.nFileSizeLow;

                    // Use apparent size rounded to the cluster
                    // boundary. Skipping GetCompressedFileSize
                    // avoids an extra syscall per file — the
                    // small accuracy loss on sparse/compressed
                    // files is not worth the 10x slowdown.
                    ulong sizeOnDisk = apparent;
                    if (!options.ApparentSize && apparent > 0)
                    {
                        ulong cluster = options.ClusterSize;
                        sizeOnDisk =
                            ((apparent + cluster - 1) / cluster)
                            * cluster;
                    }

                    dirNode.Children.Add(Node.CreateEntry(
                        name, NodeKind.File,
                        sizeOnDisk, modSecs));
                    progress.CountFile(sizeOnDisk);
                }
            }
            while (FindNextFile(handle, out findData));
        }
        finally
        {
            FindClose(handle);
        }

        return enqueued;
    }

    #endregion

    #region MFT Scanner (WizTree-style fast path)

    private static bool IsDriveRoot(string path)
    {
        return path.Length <= 3
            && path.Length >= 2
            && char.IsLetter(path[0])
            && path[1] == ':';
    }

    /// <summary>
    /// Read the NTFS MFT via FSCTL_ENUM_USN_DATA to enumerate
    /// every file on the volume in one sequential pass. Returns
    /// null if MFT access fails so the caller falls back.
    /// </summary>
    private static Node? TryMftScan(
        string rootPath,
        string rootName,
        ScanOptions options,
        ScanProgress progress,
        CancellationToken ct)
    {
        char driveLetter = char.ToUpperInvariant(rootPath[0]);

        try
        {
            var driveInfo = new DriveInfo(driveLetter.ToString());
            if (!driveInfo.IsReady
                || !string.Equals(
                    driveInfo.DriveFormat, "NTFS",
                    StringComparison.OrdinalIgnoreCase))
                return null;
        }
        catch
        {
            return null;
        }

        string volumePath = $@"\\.\{driveLetter}:";
        IntPtr hVolume = CreateFileW(
            volumePath,
            GENERIC_READ,
            FILE_SHARE_READ | FILE_SHARE_WRITE,
            IntPtr.Zero,
            OPEN_EXISTING,
            FILE_ATTRIBUTE_NORMAL,
            IntPtr.Zero);

        if (hVolume == INVALID_HANDLE_VALUE)
            return null;

        try
        {
            // Get cluster size from the volume for rounding
            uint clusterSize = QueryClusterSize(hVolume);

            return EnumerateMft(
                hVolume, driveLetter, rootName, clusterSize,
                options, progress, ct);
        }
        catch
        {
            return null;
        }
        finally
        {
            CloseHandle(hVolume);
        }
    }

    private static uint QueryClusterSize(IntPtr hVolume)
    {
        int size = Marshal.SizeOf<NTFS_VOLUME_DATA_BUFFER>();
        IntPtr buf = Marshal.AllocHGlobal(size);
        try
        {
            if (DeviceIoControl(
                    hVolume,
                    FSCTL_GET_NTFS_VOLUME_DATA,
                    IntPtr.Zero, 0,
                    buf, (uint)size,
                    out _, IntPtr.Zero))
            {
                var data = Marshal.PtrToStructure<
                    NTFS_VOLUME_DATA_BUFFER>(buf);
                if (data.BytesPerCluster > 0)
                    return data.BytesPerCluster;
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buf);
        }
        return 4096; // NTFS default
    }

    private static Node? EnumerateMft(
        IntPtr hVolume,
        char driveLetter,
        string rootName,
        uint clusterSize,
        ScanOptions options,
        ScanProgress progress,
        CancellationToken ct)
    {
        const int bufferSize = 128 * 1024;

        // Collect MFT records. Key = FileReferenceNumber (masked
        // to 48 bits to strip the sequence counter).
        var records =
            new Dictionary<ulong, MftRecord>(500_000);

        IntPtr buffer = Marshal.AllocHGlobal(bufferSize);
        try
        {
            var enumData = new MFT_ENUM_DATA_V0
            {
                StartFileReferenceNumber = 0,
                LowUsn = 0,
                HighUsn = long.MaxValue
            };

            int enumSize =
                Marshal.SizeOf<MFT_ENUM_DATA_V0>();
            IntPtr enumPtr = Marshal.AllocHGlobal(enumSize);

            try
            {
                Marshal.StructureToPtr(
                    enumData, enumPtr, false);

                while (!ct.IsCancellationRequested
                    && !progress.IsCancelled)
                {
                    bool ok = DeviceIoControl(
                        hVolume,
                        FSCTL_ENUM_USN_DATA,
                        enumPtr, (uint)enumSize,
                        buffer, (uint)bufferSize,
                        out uint returned,
                        IntPtr.Zero);

                    if (!ok || returned <= 8)
                        break;

                    ulong nextRef = (ulong)Marshal.ReadInt64(
                        buffer, 0);

                    int offset = 8;
                    while (offset + 64 <= returned)
                    {
                        var rec =
                            Marshal.PtrToStructure<USN_RECORD_V2>(
                                buffer + offset);

                        if (rec.RecordLength == 0)
                            break;

                        int nameLen = rec.FileNameLength / 2;
                        string name = nameLen > 0
                            ? Marshal.PtrToStringUni(
                                buffer + offset
                                    + rec.FileNameOffset,
                                nameLen)
                              ?? string.Empty
                            : string.Empty;

                        uint attrs = rec.FileAttributes;

                        // Mask to 48-bit file reference
                        ulong fileRef =
                            rec.FileReferenceNumber
                            & 0x0000FFFFFFFFFFFF;
                        ulong parentRef =
                            rec.ParentFileReferenceNumber
                            & 0x0000FFFFFFFFFFFF;

                        records[fileRef] = new MftRecord
                        {
                            Name = name,
                            ParentRef = parentRef,
                            IsDirectory =
                                (attrs & FILE_ATTRIBUTE_DIRECTORY)
                                != 0,
                            IsHidden =
                                (attrs & (FILE_ATTRIBUTE_HIDDEN
                                    | FILE_ATTRIBUTE_SYSTEM))
                                != 0,
                            Timestamp = rec.TimeStamp
                        };

                        offset += (int)rec.RecordLength;
                    }

                    enumData.StartFileReferenceNumber = nextRef;
                    Marshal.StructureToPtr(
                        enumData, enumPtr, true);
                }
            }
            finally
            {
                Marshal.FreeHGlobal(enumPtr);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }

        if (ct.IsCancellationRequested || progress.IsCancelled)
            return null;

        // Phase 2: Build the tree from parent references.
        // NTFS root directory has MFT reference 5.
        const ulong NTFS_ROOT_REF = 5;

        var rootNode = Node.CreateDirectory(rootName);
        var nodeMap = new Dictionary<ulong, Node>(
            records.Count);
        nodeMap[NTFS_ROOT_REF] = rootNode;

        // Create nodes
        foreach (var (refNum, rec) in records)
        {
            if (refNum == NTFS_ROOT_REF)
                continue;
            if (rec.IsHidden && !options.IncludeHidden)
                continue;

            long modSecs = FileTimeToUnixSeconds(rec.Timestamp);
            Node node;
            if (rec.IsDirectory)
            {
                node = Node.CreateDirectory(rec.Name);
                node.Modified = modSecs;
            }
            else
            {
                node = Node.CreateEntry(
                    rec.Name, NodeKind.File, 0, modSecs);
            }
            nodeMap[refNum] = node;
        }

        // Attach children to parents
        foreach (var (refNum, rec) in records)
        {
            if (refNum == NTFS_ROOT_REF)
                continue;
            if (!nodeMap.TryGetValue(refNum, out var child))
                continue;
            if (!nodeMap.TryGetValue(
                    rec.ParentRef, out var parent))
                continue;
            if (!parent.IsDirectory)
                continue;

            parent.Children.Add(child);
        }

        // Phase 3: Resolve file sizes using FindFirstFileEx per
        // directory. Much faster than per-file FileInfo because
        // we batch all files in each dir in one handle open.
        progress.SetPhase("Resolving sizes");
        ResolveFileSizesParallel(
            rootNode, $"{driveLetter}:\\",
            clusterSize, options, progress, ct);

        return rootNode;
    }

    /// <summary>
    /// Walk the tree top-down and resolve sizes for each dir's
    /// children using a single FindFirstFileEx call per directory.
    /// Uses Parallel.ForEach to spread across cores.
    /// </summary>
    private static void ResolveFileSizesParallel(
        Node root,
        string rootPath,
        uint clusterSize,
        ScanOptions options,
        ScanProgress progress,
        CancellationToken ct)
    {
        // Collect all (dirNode, fullPath) pairs
        var dirs = new List<(Node DirNode, string FullPath)>(
            50_000);
        CollectDirectories(root, rootPath, dirs);

        // Resolve in parallel
        Parallel.ForEach(
            dirs,
            new ParallelOptions
            {
                MaxDegreeOfParallelism =
                    Math.Clamp(
                        Environment.ProcessorCount * 2, 4, 64),
                CancellationToken = ct
            },
            pair =>
            {
                if (progress.IsCancelled)
                    return;
                ResolveDirectoryFileSizes(
                    pair.DirNode, pair.FullPath,
                    clusterSize, options, progress);
            });
    }

    private static void CollectDirectories(
        Node node, string path,
        List<(Node, string)> result)
    {
        result.Add((node, path));
        foreach (var child in node.Children)
        {
            if (child.IsDirectory)
            {
                CollectDirectories(
                    child,
                    Path.Combine(path, child.Name),
                    result);
            }
        }
    }

    /// <summary>
    /// For each file child of dirNode, look up its actual size
    /// from the filesystem via FindFirstFileEx. One handle open
    /// per directory — not per file.
    /// </summary>
    private static void ResolveDirectoryFileSizes(
        Node dirNode,
        string dirPath,
        uint clusterSize,
        ScanOptions options,
        ScanProgress progress)
    {
        // Build a lookup of file children by name for this dir
        var fileChildren = new Dictionary<string, Node>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var child in dirNode.Children)
        {
            if (!child.IsDirectory)
                fileChildren[child.Name] = child;
        }

        if (fileChildren.Count == 0)
            return;

        string searchDir = dirPath.StartsWith(@"\\?\")
            ? dirPath
            : @"\\?\" + dirPath;
        string pattern = searchDir.EndsWith(
                Path.DirectorySeparatorChar)
            ? searchDir + "*"
            : searchDir + Path.DirectorySeparatorChar + "*";

        IntPtr handle = FindFirstFileEx(
            pattern,
            FINDEX_INFO_LEVELS.FindExInfoBasic,
            out WIN32_FIND_DATAW fd,
            FINDEX_SEARCH_OPS.FindExSearchNameMatch,
            IntPtr.Zero,
            FIND_FIRST_EX_LARGE_FETCH);

        if (handle == INVALID_HANDLE_VALUE)
            return;

        try
        {
            do
            {
                string name = fd.cFileName;
                if (name is "." or "..")
                    continue;

                if ((fd.dwFileAttributes
                        & FILE_ATTRIBUTE_DIRECTORY) != 0)
                    continue;

                if (!fileChildren.TryGetValue(
                        name, out var node))
                    continue;

                ulong apparent =
                    ((ulong)fd.nFileSizeHigh << 32)
                    | fd.nFileSizeLow;
                ulong sized = apparent;

                if (!options.ApparentSize && apparent > 0)
                {
                    sized = ((apparent + clusterSize - 1)
                        / clusterSize) * clusterSize;
                }

                node.Bytes = sized;
                node.OwnBytes = sized;
                progress.CountFile(sized);
            }
            while (FindNextFile(handle, out fd));
        }
        finally
        {
            FindClose(handle);
        }
    }

    private sealed class MftRecord
    {
        public required string Name;
        public ulong ParentRef;
        public bool IsDirectory;
        public bool IsHidden;
        public long Timestamp;
    }

    private static long FileTimeToUnixSeconds(long fileTime)
    {
        if (fileTime <= 0) return 0;
        try
        {
            var dt = DateTime.FromFileTimeUtc(fileTime);
            return new DateTimeOffset(dt).ToUnixTimeSeconds();
        }
        catch
        {
            return 0;
        }
    }

    #endregion

    private static long ToUnixSeconds(
        System.Runtime.InteropServices.ComTypes.FILETIME ft)
    {
        long v = ((long)ft.dwHighDateTime << 32)
            | (uint)ft.dwLowDateTime;
        if (v <= 0) return 0;
        try
        {
            var dt = DateTime.FromFileTimeUtc(v);
            return new DateTimeOffset(dt).ToUnixTimeSeconds();
        }
        catch
        {
            return 0;
        }
    }
}
