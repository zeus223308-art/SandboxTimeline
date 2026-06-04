using System.Security.Cryptography;
using System.Text;

namespace SandboxTimeline;

/// <summary>
/// Produces a one-way SHA-256 fingerprint of a license key for vault-side Stripe metadata matching.
/// The raw license key never leaves activation memory except during the initial user input event.
/// </summary>
internal static class LicenseKeyHasher
{
    public static string ComputeHash(string licenseKey)
    {
        if (string.IsNullOrWhiteSpace(licenseKey))
        {
            return string.Empty;
        }

        var normalized = licenseKey.Trim();
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
