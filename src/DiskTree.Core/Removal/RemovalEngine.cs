using System.Runtime.InteropServices;

namespace DiskTree.Core.Removal;

public static class RemovalEngine
{
    private static readonly string[] ProtectedSystemFolders =
    [
        Environment.GetFolderPath(Environment.SpecialFolder.Windows),
        Environment.GetFolderPath(Environment.SpecialFolder.System),
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
    ];

    public static string Normalize(string path)
    {
        try
        {
            return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return path;
        }
    }

    public static string? Refuse(string path, string scannedRoot)
    {
        string normPath = Normalize(path);
        string normRoot = Normalize(scannedRoot);
        string? userProfile = Environment.GetEnvironmentVariable("USERPROFILE");
        string normUser = !string.IsNullOrEmpty(userProfile) ? Normalize(userProfile) : string.Empty;

        // 1. Filesystem root / drive root
        string? pathRoot = Path.GetPathRoot(normPath);
        if (string.Equals(normPath, pathRoot?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
            return "the drive root cannot be removed";

        // 2. Scanned root itself
        if (string.Equals(normPath, normRoot, StringComparison.OrdinalIgnoreCase))
            return "the scanned root cannot be removed";

        // 3. User profile folder root
        if (!string.IsNullOrEmpty(normUser) && string.Equals(normPath, normUser, StringComparison.OrdinalIgnoreCase))
            return "the user profile directory cannot be removed";

        // 4. Must be under scanned root
        if (!normPath.StartsWith(normRoot, StringComparison.OrdinalIgnoreCase) ||
            (normPath.Length > normRoot.Length && normPath[normRoot.Length] != Path.DirectorySeparatorChar))
        {
            return "outside the scanned root";
        }

        // 5. Windows system protected folders
        foreach (var protectedFolder in ProtectedSystemFolders)
        {
            if (string.IsNullOrEmpty(protectedFolder)) continue;
            string normProtected = Normalize(protectedFolder);
            if (normPath.StartsWith(normProtected, StringComparison.OrdinalIgnoreCase))
                return $"part of protected Windows system location: {protectedFolder}";
        }

        // 6. Recycler or System Volume Information
        string name = Path.GetFileName(normPath);
        if (string.Equals(name, "$Recycle.Bin", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(name, "System Volume Information", StringComparison.OrdinalIgnoreCase))
        {
            return "system storage directory";
        }

        return null;
    }

    public static RemovalPlan BuildPlan(IEnumerable<Target> targets, string scannedRoot)
    {
        var plan = new RemovalPlan();
        var accepted = new List<Target>();

        foreach (var target in targets)
        {
            string normPath = Normalize(target.Path);
            string? refusal = Refuse(normPath, scannedRoot);
            if (refusal != null)
            {
                plan.Blocked.Add(new Blocked(target.Path, refusal));
            }
            else
            {
                accepted.Add(target with { Path = normPath });
            }
        }

        // Sort by path length ascending so outer directories appear before inner ones
        accepted.Sort((a, b) => string.Compare(a.Path, b.Path, StringComparison.OrdinalIgnoreCase));

        var outer = new List<Target>();
        foreach (var target in accepted)
        {
            // If target is inside an existing outer target, mark as covered
            bool isCovered = outer.Any(candidate =>
                target.Path.StartsWith(candidate.Path + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));

            if (isCovered)
                plan.Covered.Add(target);
            else
                outer.Add(target);
        }

        plan.Targets = outer;
        return plan;
    }

    public static async Task ExecuteAsync(
        RemovalPlan plan,
        RemovalMode mode,
        IProgress<(string Path, bool Success, string? Error)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        await Task.Run(() =>
        {
            foreach (var target in plan.Targets)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    if (mode == RemovalMode.RecycleBin)
                    {
                        bool success = SendToRecycleBin(target.Path);
                        if (success)
                        {
                            progress?.Report((target.Path, true, null));
                        }
                        else
                        {
                            progress?.Report((target.Path, false, "Windows Shell failed to recycle the path"));
                        }
                    }
                    else
                    {
                        if (target.IsDirectory)
                            Directory.Delete(target.Path, recursive: true);
                        else
                            File.Delete(target.Path);

                        progress?.Report((target.Path, true, null));
                    }
                }
                catch (Exception ex)
                {
                    progress?.Report((target.Path, false, ex.Message));
                }
            }
        }, cancellationToken);
    }

    #region Win32 Shell Recycle Bin

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEOPSTRUCT
    {
        public IntPtr hwnd;
        public uint wFunc;
        public string pFrom;
        public string? pTo;
        public ushort fFlags;
        [MarshalAs(UnmanagedType.Bool)]
        public bool fAnyOperationsAborted;
        public IntPtr hNameMappings;
        public string? lpszProgressTitle;
    }

    private const uint FO_DELETE = 0x0003;
    private const ushort FOF_ALLOWUNDO = 0x0040;
    private const ushort FOF_NOCONFIRMATION = 0x0010;
    private const ushort FOF_SILENT = 0x0004;
    private const ushort FOF_NOERRORUI = 0x0400;

    [DllImport("shell32.dll", EntryPoint = "SHFileOperationW", CharSet = CharSet.Unicode)]
    private static extern int SHFileOperation(ref SHFILEOPSTRUCT lpFileOp);

    private static bool SendToRecycleBin(string path)
    {
        // SHFileOperation requires double null-terminated string
        string doubleNullPath = path + "\0\0";

        var fileOp = new SHFILEOPSTRUCT
        {
            wFunc = FO_DELETE,
            pFrom = doubleNullPath,
            fFlags = FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_SILENT | FOF_NOERRORUI
        };

        int result = SHFileOperation(ref fileOp);
        return result == 0 && !fileOp.fAnyOperationsAborted;
    }

    #endregion
}
