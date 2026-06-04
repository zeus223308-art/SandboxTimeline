using System.Text.Json;

namespace SandboxTimeline;

public static class UserGuidePreferences
{
    private static readonly string PreferencesDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SandboxTimeline");

    private static readonly string PreferencesFilePath = Path.Combine(
        PreferencesDirectory,
        "user-guide.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public static bool ShouldShowWelcomeGuide()
    {
        var state = Load();
        return !state.SkipWelcomeGuide;
    }

    public static void SetSkipWelcomeGuide(bool skip)
    {
        var state = Load();
        state.SkipWelcomeGuide = skip;
        Save(state);
    }

    private static UserGuideState Load()
    {
        try
        {
            if (!File.Exists(PreferencesFilePath))
            {
                return new UserGuideState();
            }

            var json = File.ReadAllText(PreferencesFilePath);
            return JsonSerializer.Deserialize<UserGuideState>(json, JsonOptions) ?? new UserGuideState();
        }
        catch
        {
            return new UserGuideState();
        }
    }

    private static void Save(UserGuideState state)
    {
        Directory.CreateDirectory(PreferencesDirectory);
        var json = JsonSerializer.Serialize(state, JsonOptions);
        File.WriteAllText(PreferencesFilePath, json);
    }

    private sealed class UserGuideState
    {
        public bool SkipWelcomeGuide { get; set; }
    }
}
