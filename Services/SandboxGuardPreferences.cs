using System.Text.Json;

namespace SandboxTimeline;

public static class SandboxGuardPreferences
{
    private static readonly string PreferencesDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SandboxTimeline");

    private static readonly string PreferencesFilePath = Path.Combine(
        PreferencesDirectory,
        "sandbox-guard.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public static SandboxGuardState Load()
    {
        try
        {
            if (!File.Exists(PreferencesFilePath))
            {
                return new SandboxGuardState();
            }

            var json = File.ReadAllText(PreferencesFilePath);
            return JsonSerializer.Deserialize<SandboxGuardState>(json, JsonOptions) ?? new SandboxGuardState();
        }
        catch
        {
            return new SandboxGuardState();
        }
    }

    public static void Save(string? watchFolderPath, bool guardEnabled)
    {
        var state = new SandboxGuardState
        {
            WatchFolderPath = watchFolderPath,
            GuardEnabled = guardEnabled
        };

        Directory.CreateDirectory(PreferencesDirectory);
        var json = JsonSerializer.Serialize(state, JsonOptions);
        File.WriteAllText(PreferencesFilePath, json);
    }

    public sealed class SandboxGuardState
    {
        public string? WatchFolderPath { get; set; }

        public bool GuardEnabled { get; set; }
    }
}
