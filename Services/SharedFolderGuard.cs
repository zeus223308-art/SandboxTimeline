namespace SandboxTimeline;

/// <summary>
/// Provides a persistent host folder mapped into Windows Sandbox so files can be rescued.
/// </summary>
public static class SharedFolderGuard
{
    public const string SharedFolderName = "Sandbox_Shared";

    public static string GetSharedFolderPath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
            SharedFolderName);
    }

    public static void EnsureSharedFolderExists()
    {
        Directory.CreateDirectory(GetSharedFolderPath());
    }
}
