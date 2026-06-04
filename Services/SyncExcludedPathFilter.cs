namespace SandboxTimeline;

internal static class SyncExcludedPathFilter
{
    private static readonly HashSet<string> SkipDirectoryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Temp",
        "Tmp",
        "INetCache",
        "OneDriveTemp",
        ".wsync",
        "SyncEngineDatabase",
        "ClientPolicy",
        "node_modules",
        ".git",
        "cache",
        "Packages",
        "SandboxTimeline",
        "Logs"
    };

    private static readonly HashSet<string> SkipFileExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".lnk",
        ".tmp",
        ".temp",
        ".part",
        ".odtmp",
        ".download",
        ".crdownload",
        ".partial",
        ".sync",
        ".lock"
    };

    private static readonly HashSet<string> SkipExactFileNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "desktop.ini",
        "Thumbs.db",
        ".DS_Store"
    };

    private static readonly Lazy<string[]> SystemTempRoots = new(ResolveSystemTempRoots);

    public static bool ShouldIgnorePath(string? fullPath)
    {
        if (string.IsNullOrWhiteSpace(fullPath))
        {
            return true;
        }

        string normalized;
        try
        {
            normalized = Path.GetFullPath(fullPath);
        }
        catch
        {
            return true;
        }

        if (IsUnderSystemTempRoot(normalized))
        {
            return true;
        }

        if (ContainsIgnoredDirectorySegment(normalized))
        {
            return true;
        }

        if (IsOneDrivePath(normalized) && ShouldIgnoreOneDriveArtifact(normalized))
        {
            return true;
        }

        return ShouldIgnoreFileName(Path.GetFileName(normalized));
    }

    public static bool ShouldIgnoreTrackedEntry(TrackedFileEntry entry) =>
        ShouldIgnorePath(string.IsNullOrWhiteSpace(entry.FullPath) ? entry.RelativePath : entry.FullPath);

    private static string[] ResolveSystemTempRoots()
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Path.GetTempPath()
        };

        AddIfExists(roots, Environment.GetEnvironmentVariable("TEMP"));
        AddIfExists(roots, Environment.GetEnvironmentVariable("TMP"));
        AddIfExists(roots, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp"));
        AddIfExists(roots, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp"));
        AddIfExists(roots, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "OneDrive", "logs"));
        AddIfExists(roots, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "OneDrive", "cache"));

        return roots.ToArray();
    }

    private static void AddIfExists(ISet<string> roots, string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            roots.Add(Path.GetFullPath(path).TrimEnd('\\'));
        }
        catch
        {
        }
    }

    private static bool IsUnderSystemTempRoot(string normalizedPath)
    {
        foreach (var root in SystemTempRoots.Value)
        {
            if (normalizedPath.StartsWith(root + "\\", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(normalizedPath, root, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsIgnoredDirectorySegment(string normalizedPath)
    {
        var segments = normalizedPath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return segments.Any(segment => SkipDirectoryNames.Contains(segment));
    }

    private static bool IsOneDrivePath(string normalizedPath) =>
        normalizedPath.Contains(@"\OneDrive\", StringComparison.OrdinalIgnoreCase) ||
        normalizedPath.Contains(@"\OneDrive - ", StringComparison.OrdinalIgnoreCase) ||
        normalizedPath.EndsWith(@"\OneDrive", StringComparison.OrdinalIgnoreCase);

    private static bool ShouldIgnoreOneDriveArtifact(string normalizedPath)
    {
        var fileName = Path.GetFileName(normalizedPath);
        if (ShouldIgnoreFileName(fileName))
        {
            return true;
        }

        if (fileName.StartsWith('~') || fileName.StartsWith("~$", StringComparison.Ordinal))
        {
            return true;
        }

        if (fileName.Contains(".tmp.", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return fileName.EndsWith("~", StringComparison.Ordinal);
    }

    private static bool ShouldIgnoreFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return false;
        }

        if (SkipExactFileNames.Contains(fileName))
        {
            return true;
        }

        var extension = Path.GetExtension(fileName);
        return !string.IsNullOrEmpty(extension) && SkipFileExtensions.Contains(extension);
    }
}
