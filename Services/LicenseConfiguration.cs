namespace SandboxTimeline;

internal static class ProductionLicenseConfiguration
{
    public const string LicenseVaultUrlEnvironmentVariable = "SANDBOXTIMELINE_LICENSE_VAULT_URL";
    public const string LicenseVaultSecretEnvironmentVariable = "SANDBOXTIMELINE_LICENSE_VAULT_SECRET";
    public const string DefaultProductionVaultEndpointUrl = "https://license-vault-gules.vercel.app/v1/license/validate";
    public const string ProductMetadataId = "prod_sandbox_timeline_premium_v1";
    public const string ProductCode = "sandbox_timeline";
    public const string ClientVersion = "1.0.5";

    public static string ResolveVaultEndpointUrl(string? overrideUrl = null)
    {
        if (!string.IsNullOrWhiteSpace(overrideUrl))
        {
            return overrideUrl.Trim();
        }

        var fromEnvironment = Environment.GetEnvironmentVariable(LicenseVaultUrlEnvironmentVariable)?.Trim();
        return string.IsNullOrWhiteSpace(fromEnvironment)
            ? DefaultProductionVaultEndpointUrl
            : fromEnvironment;
    }
}

#if DEBUG
internal static class DeveloperLicenseResetPolicy
{
    /// <summary>
    /// TEMPORARY: Deletes encrypted local premium token on every launch so license
    /// failure/retry UX can be tested from a clean Free state. Set to false when done.
    /// </summary>
    public static readonly bool ForceClearStoredLicenseOnStartup = false;
}
#endif

#if DEBUG
internal static class DebugLicenseConfiguration
{
    public const string InstantPremiumTestKey = "DEBUG-PREMIUM-TEST";
    public const string DebugSubscriptionId = "sub_debug_local_only";
    public const string DebugCustomerId = "cus_debug_local_only";

    public static bool IsDebugPremiumTestKey(string licenseKey) =>
        string.Equals(licenseKey.Trim(), InstantPremiumTestKey, StringComparison.Ordinal);
}
#endif
