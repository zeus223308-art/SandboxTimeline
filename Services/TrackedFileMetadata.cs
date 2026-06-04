using System.Security.Cryptography;
using System.Text;

namespace SandboxTimeline;

internal static class TrackedFileMetadata
{
    public static TrackedFileEntry CreateEntry(string fullPath, long usn, uint reason, bool changed)
    {
        var info = new FileInfo(fullPath);
        var relative = ToRelativeProtectedPath(fullPath);
        var hash = ComputeMetadataHash(fullPath, info.Length, info.LastWriteTimeUtc);

        return new TrackedFileEntry
        {
            RelativePath = relative,
            FullPath = fullPath,
            SizeBytes = info.Length,
            LastWriteUtcTicks = info.LastWriteTimeUtc.Ticks,
            MetadataHash = hash,
            Usn = usn,
            ChangeReason = reason,
            ChangedSincePrevious = changed
        };
    }

    public static string ToRelativeProtectedPath(string fullPath)
    {
        var root = Path.GetPathRoot(fullPath) ?? "C:\\";
        return fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase)
            ? fullPath[root.Length..].TrimStart('\\')
            : fullPath;
    }

    public static string ComputeMetadataHash(string path, long size, DateTime lastWriteUtc)
    {
        var hashSourceText = $"{path}|{size}|{lastWriteUtc.Ticks}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(hashSourceText));
        return Convert.ToHexString(bytes)[..16];
    }
}
