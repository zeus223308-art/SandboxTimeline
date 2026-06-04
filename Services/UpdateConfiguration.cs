namespace SandboxTimeline;

/// <summary>
/// Remote update manifest and package endpoints for the silent auto-patcher.
/// </summary>
internal static class UpdateConfiguration
{
    public const string VersionManifestUrlEnvironmentVariable = "SANDBOXTIMELINE_UPDATE_VERSION_URL";
    public const string UpdateEnabledEnvironmentVariable = "SANDBOXTIMELINE_UPDATE_ENABLED";
    public const string DefaultVersionManifestUrl = "https://raw.githubusercontent.com/zeus223308-art/SandboxTimeline/master/docs/version.txt";
    public const string SkipUpdateArgument = "--skip-update";

    public static bool IsAutoUpdateEnabled()
    {
        var fromEnvironment = ReadOptionalBooleanEnvironmentVariable(UpdateEnabledEnvironmentVariable);
        return fromEnvironment ?? false;
    }

    public static string ResolveVersionManifestUrl()
    {
        var fromEnvironment = Environment.GetEnvironmentVariable(VersionManifestUrlEnvironmentVariable)?.Trim();
        return string.IsNullOrWhiteSpace(fromEnvironment)
            ? DefaultVersionManifestUrl
            : fromEnvironment;
    }

    /// <summary>
    /// Silent in-place updates are unsafe under sync folders (OneDrive) and often look like an instant crash.
    /// </summary>
    public static bool IsUnsafeAutoUpdateTargetDirectory(string targetDirectory)
    {
        if (string.IsNullOrWhiteSpace(targetDirectory))
        {
            return true;
        }

        if (ContainsUnsafeSyncFolderMarker(targetDirectory))
        {
            return true;
        }

        try
        {
            var fullPath = Path.GetFullPath(targetDirectory);
            return ContainsUnsafeSyncFolderMarker(fullPath);
        }
        catch
        {
            return true;
        }
    }

    private static bool ContainsUnsafeSyncFolderMarker(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return true;
        }

        return path.Contains("OneDrive", StringComparison.OrdinalIgnoreCase) ||
               path.Contains("iCloudDrive", StringComparison.OrdinalIgnoreCase) ||
               path.Contains("Dropbox", StringComparison.OrdinalIgnoreCase) ||
               path.Contains("Google Drive", StringComparison.OrdinalIgnoreCase);
    }

    private static bool? ReadOptionalBooleanEnvironmentVariable(string variableName)
    {
        var rawValue = Environment.GetEnvironmentVariable(variableName)?.Trim();
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return null;
        }

        if (bool.TryParse(rawValue, out var parsed))
        {
            return parsed;
        }

        return rawValue is "1" or "yes" or "YES" or "on" or "ON";
    }
}
