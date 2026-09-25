using System.Runtime.InteropServices;

namespace DiskTree.Core.Space;

public readonly record struct SpaceInfo(ulong Total, ulong Free, ulong Available)
{
    public ulong Used => Total > Free ? Total - Free : 0;

    public float UsedFraction => Total == 0 ? 0f : (float)((double)Used / Total);

    public SpaceInfo AfterRemoving(ulong bytes)
    {
        ulong newAvailable = Math.Min(Total, Available + bytes);
        ulong newFree = Math.Min(Total, Free + bytes);
        return new SpaceInfo(Total, newFree, newAvailable);
    }
}

public static class VolumeSpace
{
    [DllImport("kernel32.dll", EntryPoint = "GetDiskFreeSpaceExW", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetDiskFreeSpaceEx(
        string lpDirectoryName,
        out ulong lpFreeBytesAvailableToCaller,
        out ulong lpTotalNumberOfBytes,
        out ulong lpTotalNumberOfFreeBytes);

    public static SpaceInfo? GetSpaceInfo(string directoryPath)
    {
        try
        {
            if (GetDiskFreeSpaceEx(directoryPath, out ulong available, out ulong total, out ulong free))
            {
                return new SpaceInfo(total, free, available);
            }

            // Fallback to DriveInfo
            var root = Path.GetPathRoot(directoryPath);
            if (!string.IsNullOrEmpty(root))
            {
                var drive = new DriveInfo(root);
                if (drive.IsReady)
                {
                    return new SpaceInfo(
                        (ulong)Math.Max(0, drive.TotalSize),
                        (ulong)Math.Max(0, drive.TotalFreeSpace),
                        (ulong)Math.Max(0, drive.AvailableFreeSpace)
                    );
                }
            }
        }
        catch
        {
            // Ignore and return null
        }

        return null;
    }
}
