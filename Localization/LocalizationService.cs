using System.Globalization;
using System.Windows;

namespace SandboxTimeline;

public static class LocalizationService
{
    private const string EnglishResource = "Localization/Strings.en.xaml";
    private const string KoreanResource = "Localization/Strings.ko.xaml";

    private static ResourceDictionary? _stringsDictionary;

    public static string CurrentLanguageCode { get; private set; } = "en";

    public static void ApplyStartupCulture()
    {
        var languageCode = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        var resourcePath = string.Equals(languageCode, "ko", StringComparison.OrdinalIgnoreCase)
            ? KoreanResource
            : EnglishResource;

        CurrentLanguageCode = string.Equals(languageCode, "ko", StringComparison.OrdinalIgnoreCase) ? "ko" : "en";
        MergeStringsDictionary(resourcePath);
    }

    private static void MergeStringsDictionary(string resourcePath)
    {
        if (System.Windows.Application.Current == null)
        {
            return;
        }

        if (_stringsDictionary != null)
        {
            System.Windows.Application.Current.Resources.MergedDictionaries.Remove(_stringsDictionary);
            _stringsDictionary = null;
        }

        _stringsDictionary = new ResourceDictionary
        {
            Source = new Uri($"pack://application:,,,/{resourcePath}", UriKind.Absolute)
        };

        System.Windows.Application.Current.Resources.MergedDictionaries.Add(_stringsDictionary);
    }
}

public static class Loc
{
    public static string Get(string key)
    {
        if (System.Windows.Application.Current?.TryFindResource(key) is string value)
        {
            return value;
        }

        return key;
    }

    public static string Format(string key, params object[] args)
    {
        return string.Format(CultureInfo.CurrentCulture, Get(key), args);
    }
}
