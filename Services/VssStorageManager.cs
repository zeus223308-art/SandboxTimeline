namespace SandboxTimeline;

/// <summary>
/// Caps snapshot backup disk usage and reclaims space with FIFO deletion.
/// </summary>
public sealed class VssStorageManager
{
    public const long FixedCapBytes = 20L * 1024 * 1024 * 1024;
    public const double DriveCapFraction = 0.10;

    public long GetStorageCapBytes(string volumePath = @"C:\")
    {
        try
        {
            var root = Path.GetPathRoot(volumePath) ?? @"C:\";
            var drive = new DriveInfo(root);
            if (!drive.IsReady)
            {
                return FixedCapBytes;
            }

            var tenPercentCap = (long)(drive.TotalSize * DriveCapFraction);
            return Math.Min(tenPercentCap, FixedCapBytes);
        }
        catch
        {
            return FixedCapBytes;
        }
    }

    public long GetDirectorySizeBytes(string rootDirectory)
    {
        if (!Directory.Exists(rootDirectory))
        {
            return 0;
        }

        long total = 0;
        var pending = new Stack<string>();
        pending.Push(rootDirectory);

        while (pending.Count > 0)
        {
            var current = pending.Pop();

            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(current);
            }
            catch
            {
                continue;
            }

            foreach (var file in files)
            {
                try
                {
                    total += new FileInfo(file).Length;
                }
                catch
                {
                }
            }

            IEnumerable<string> directories;
            try
            {
                directories = Directory.EnumerateDirectories(current);
            }
            catch
            {
                continue;
            }

            foreach (var directory in directories)
            {
                pending.Push(directory);
            }
        }

        return total;
    }

    public int EnforceQuota(
        string backupRoot,
        IReadOnlyList<SnapshotInfo> snapshotsNewestFirst,
        Action<SnapshotInfo> deleteSnapshot)
    {
        var capBytes = GetStorageCapBytes();
        var usageBytes = GetDirectorySizeBytes(backupRoot);
        if (usageBytes <= capBytes)
        {
            return 0;
        }

        var removed = 0;
        foreach (var snapshot in snapshotsNewestFirst.OrderBy(static s => s.CreatedAtUtc))
        {
            if (usageBytes <= capBytes)
            {
                break;
            }

            deleteSnapshot(snapshot);
            removed++;
            usageBytes = GetDirectorySizeBytes(backupRoot);
        }

        return removed;
    }
}
