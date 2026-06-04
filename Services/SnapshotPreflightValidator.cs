using System.Security.Principal;

namespace SandboxTimeline;

public static class SnapshotPreflightValidator
{
    private const long MinimumFreeBytes = 64L * 1024 * 1024;

    public static void ValidateOrThrow(string backupRoot)
    {
        if (!IsRunningAsAdministrator())
        {
            throw new UnauthorizedAccessException(
                "Administrator privileges are required to read the USN journal and create VSS snapshots.");
        }

        var root = Path.GetPathRoot(backupRoot) ?? "C:\\";
        var drive = new DriveInfo(root);
        if (!drive.IsReady)
        {
            throw new IOException($"Drive {root} is not ready.");
        }

        if (drive.AvailableFreeSpace < MinimumFreeBytes)
        {
            throw new IOException(
                $"Insufficient disk space on {root}. At least {MinimumFreeBytes / (1024 * 1024)} MB free space is required.");
        }
    }

    public static bool IsRunningAsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }
}
