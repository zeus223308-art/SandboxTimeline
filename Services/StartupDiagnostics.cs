namespace SandboxTimeline;

internal static class StartupDiagnostics
{
    private static readonly object WriteLock = new();
    private static string? _logPath;

    public static string LogPath =>
        _logPath ??= Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "SandboxTimeline",
            "startup.log");

    public static void Log(string step, Exception? exception = null)
    {
        try
        {
            var directory = Path.GetDirectoryName(LogPath)!;
            Directory.CreateDirectory(directory);

            var line = exception == null
                ? $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {step}"
                : $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {step}{Environment.NewLine}{exception}";

            lock (WriteLock)
            {
                File.AppendAllText(LogPath, line + Environment.NewLine);
            }
        }
        catch
        {
        }
    }
}
