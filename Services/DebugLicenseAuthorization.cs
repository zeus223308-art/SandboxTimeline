#if DEBUG
namespace SandboxTimeline;

internal static class DebugLicenseAuthorization
{
    public static bool TryCreatePremiumState(string licenseKey, out LicenseManager.LicenseState state)
    {
        if (!DebugLicenseConfiguration.IsDebugPremiumTestKey(licenseKey))
        {
            state = LicenseManager.LicenseState.Free();
            return false;
        }

        state = new LicenseManager.LicenseState(
            IsValid: true,
            IsPremium: true,
            TrialSandboxEnabled: false,
            ExpiresAtUtc: DateTime.UtcNow.AddYears(10),
            SubscriptionId: DebugLicenseConfiguration.DebugSubscriptionId,
            CustomerId: DebugLicenseConfiguration.DebugCustomerId,
            LastErrorWasNetwork: false);
        return true;
    }

    public static bool IsDebugStoredToken(StoredLicenseToken token) =>
        string.Equals(token.SubscriptionId, DebugLicenseConfiguration.DebugSubscriptionId, StringComparison.Ordinal);
}
#endif
