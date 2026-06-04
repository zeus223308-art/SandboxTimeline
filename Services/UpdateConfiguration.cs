namespace SandboxTimeline;

/// <summary>
/// Remote update manifest and package endpoints for the silent auto-patcher.
/// </summary>
internal static class UpdateConfiguration
{
    public const string VersionManifestUrlEnvironmentVariable = "SANDBOXTIMELINE_UPDATE_VERSION_URL";
    public const string UpdateEnabledEnvironmentVariable = "SANDBOXTIMELINE_UPDATE_ENABLED";
    public const string DefaultVersionManifestUrl = "https://releases.sandboxtimeline.app/version.txt";
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
