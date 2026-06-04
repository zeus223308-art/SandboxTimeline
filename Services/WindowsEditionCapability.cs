using Microsoft.Win32;

namespace SandboxTimeline;

/// <summary>
/// Reads Windows edition information from registry and environment to determine sandbox support.
/// </summary>
public sealed class WindowsEditionCapability
{
    public bool IsWindows11 { get; private init; }

    public bool IsHomeEdition { get; private init; }

    public bool SupportsSandboxGuard { get; private init; }

    public string EditionId { get; private init; } = string.Empty;

    public string ProductName { get; private init; } = string.Empty;

    public static WindowsEditionCapability Evaluate()
    {
        var editionId = ReadRegistryString(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "EditionID");
        var productName = ReadRegistryString(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "ProductName");
        var isWindows11 = Environment.OSVersion.Version.Build >= 22000;
        var isHomeEdition = IsHomeEditionId(editionId) || ContainsHomeEditionMarker(productName);
        var supportsSandboxGuard = !(isWindows11 && isHomeEdition);

        return new WindowsEditionCapability
        {
            IsWindows11 = isWindows11,
            IsHomeEdition = isHomeEdition,
            SupportsSandboxGuard = supportsSandboxGuard,
            EditionId = editionId,
            ProductName = productName
        };
    }

    private static bool IsHomeEditionId(string editionId)
    {
        if (string.IsNullOrWhiteSpace(editionId))
        {
            return false;
        }

        return editionId.Equals("Core", StringComparison.OrdinalIgnoreCase)
               || editionId.Equals("CoreN", StringComparison.OrdinalIgnoreCase)
               || editionId.Equals("CoreSingleLanguage", StringComparison.OrdinalIgnoreCase)
               || editionId.Equals("CoreCountrySpecific", StringComparison.OrdinalIgnoreCase)
               || editionId.Equals("Home", StringComparison.OrdinalIgnoreCase)
               || editionId.Equals("HomeN", StringComparison.OrdinalIgnoreCase)
               || editionId.Equals("HomeSingleLanguage", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsHomeEditionMarker(string productName)
    {
        if (string.IsNullOrWhiteSpace(productName))
        {
            return false;
        }

        return productName.Contains("Home", StringComparison.OrdinalIgnoreCase)
               && !productName.Contains("Pro", StringComparison.OrdinalIgnoreCase)
               && !productName.Contains("Enterprise", StringComparison.OrdinalIgnoreCase)
               && !productName.Contains("Education", StringComparison.OrdinalIgnoreCase);
    }

    private static string ReadRegistryString(string subKeyPath, string valueName)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(subKeyPath, false);
            return key?.GetValue(valueName)?.ToString()?.Trim() ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }
}
