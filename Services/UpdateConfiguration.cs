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
#if DEBUG
        var fromEnvironment = ReadOptionalBooleanEnvironmentVariable(UpdateEnabledEnvironmentVariable);
        return fromEnvironment ?? false;
#else
        var fromEnvironment = ReadOptionalBooleanEnvironmentVariable(UpdateEnabledEnvironmentVariable);
        return fromEnvironment ?? true;
#endif
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

        try
        {
            var fullPath = Path.GetFullPath(targetDirectory);
            if (fullPath.Contains("OneDrive", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (fullPath.Contains("iCloudDrive", StringComparison.OrdinalIgnoreCase) ||
                fullPath.Contains("Dropbox", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        catch
        {
            return true;
        }

        return false;
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
