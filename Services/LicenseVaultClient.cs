using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SandboxTimeline;

/// <summary>
/// Client-side license vault handler. Validates license key hashes and product metadata identifiers
/// against the remote vault API. No Stripe secret keys or direct Stripe SDK calls exist in the client.
/// </summary>
public sealed class LicenseVaultClient : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly string _vaultEndpointUrl;
    private readonly bool _ownsHttpClient;

    public LicenseVaultClient(HttpClient? httpClient = null, string? vaultEndpointUrl = null)
    {
        _ownsHttpClient = httpClient == null;
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
        _vaultEndpointUrl = vaultEndpointUrl ?? ProductionLicenseConfiguration.ResolveVaultEndpointUrl();
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_vaultEndpointUrl);

    public async Task<VaultLicenseValidationResult> ValidateLicenseKeyAsync(
        string licenseKey,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(licenseKey))
        {
            return VaultLicenseValidationResult.Revoked("License key is empty.");
        }

        var licenseKeyHash = LicenseKeyHasher.ComputeHash(licenseKey);
        return await ValidateHashAsync(licenseKeyHash, cancellationToken).ConfigureAwait(false);
    }

    public Task<VaultLicenseValidationResult> ValidateStoredTokenAsync(
        StoredLicenseToken storedToken,
        CancellationToken cancellationToken = default)
    {
        var licenseKeyHash = ResolveStoredLicenseKeyHash(storedToken);
        if (string.IsNullOrWhiteSpace(licenseKeyHash))
        {
            return Task.FromResult(VaultLicenseValidationResult.Revoked(
                "Stored license token is missing a license key hash."));
        }

        return ValidateHashAsync(licenseKeyHash, cancellationToken);
    }

    private async Task<VaultLicenseValidationResult> ValidateHashAsync(
        string licenseKeyHash,
        CancellationToken cancellationToken)
    {
        if (!IsConfigured)
        {
            return VaultLicenseValidationResult.NotConfigured(
                "License vault endpoint URL is not configured.");
        }

        try
        {
            var requestBody = JsonSerializer.Serialize(new VaultValidationRequest
            {
                LicenseKeyHash = licenseKeyHash,
                ProductMetadataId = ProductionLicenseConfiguration.ProductMetadataId,
                MachineId = ComputeMachineFingerprint(),
                Product = ProductionLicenseConfiguration.ProductCode,
                Version = ProductionLicenseConfiguration.ClientVersion
            });

            using var request = new HttpRequestMessage(HttpMethod.Post, _vaultEndpointUrl)
            {
                Content = new StringContent(requestBody, Encoding.UTF8, "application/json")
            };

            var vaultSecret = Environment.GetEnvironmentVariable(
                ProductionLicenseConfiguration.LicenseVaultSecretEnvironmentVariable)?.Trim();
            if (!string.IsNullOrWhiteSpace(vaultSecret))
            {
                request.Headers.TryAddWithoutValidation("X-License-Vault-Secret", vaultSecret);
            }

            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return VaultLicenseValidationResult.Revoked(
                    $"License vault returned HTTP {(int)response.StatusCode}.");
            }

            return ParseVaultResponse(json);
        }
        catch (HttpRequestException ex)
        {
            return VaultLicenseValidationResult.NetworkError(
                $"License vault is unreachable: {ex.Message}");
        }
        catch (TaskCanceledException)
        {
            return VaultLicenseValidationResult.NetworkError(
                "License vault request timed out.");
        }
        catch (Exception ex)
        {
            return VaultLicenseValidationResult.Revoked(
                $"License vault validation failed: {ex.Message}");
        }
    }

    private static VaultLicenseValidationResult ParseVaultResponse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        var valid = root.TryGetProperty("valid", out var validProperty) && validProperty.GetBoolean();
        var premium = root.TryGetProperty("premium", out var premiumProperty) && premiumProperty.GetBoolean();

#if DEBUG
        var entitled = valid || premium;
#else
        var entitled = valid && premium;
#endif

        if (!entitled)
        {
            return VaultLicenseValidationResult.Revoked(
                "License vault rejected the license hash for this product metadata ID.");
        }

        var productMetadataId = root.TryGetProperty("product_metadata_id", out var productProperty)
            ? productProperty.GetString()
            : null;

        if (!string.IsNullOrWhiteSpace(productMetadataId)
            && !string.Equals(
                productMetadataId,
                ProductionLicenseConfiguration.ProductMetadataId,
                StringComparison.OrdinalIgnoreCase))
        {
            return VaultLicenseValidationResult.Revoked(
                "License vault product metadata ID mismatch.");
        }

        DateTime? expiresAtUtc = null;
        if (root.TryGetProperty("expires_at", out var expiresProperty)
            && DateTime.TryParse(expiresProperty.GetString(), out var parsedExpiry))
        {
            expiresAtUtc = parsedExpiry.ToUniversalTime();
        }

        var subscriptionId = root.TryGetProperty("subscription_id", out var subscriptionProperty)
            ? subscriptionProperty.GetString()
            : null;
        var customerId = root.TryGetProperty("customer_id", out var customerProperty)
            ? customerProperty.GetString()
            : null;

        return VaultLicenseValidationResult.Entitled(
            expiresAtUtc,
            subscriptionId,
            customerId,
            productMetadataId ?? ProductionLicenseConfiguration.ProductMetadataId);
    }

    private static string ResolveStoredLicenseKeyHash(StoredLicenseToken storedToken)
    {
        if (!string.IsNullOrWhiteSpace(storedToken.LicenseKeyHash))
        {
            return storedToken.LicenseKeyHash;
        }

        if (!string.IsNullOrWhiteSpace(storedToken.LicenseKey))
        {
            return LicenseKeyHasher.ComputeHash(storedToken.LicenseKey);
        }

        return string.Empty;
    }

    private static string ComputeMachineFingerprint()
    {
        var raw = $"{Environment.MachineName}-{Environment.UserName}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(hash)[..32].ToLowerInvariant();
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }

    private sealed class VaultValidationRequest
    {
        public string LicenseKeyHash { get; init; } = string.Empty;

        public string ProductMetadataId { get; init; } = string.Empty;

        public string MachineId { get; init; } = string.Empty;

        public string Product { get; init; } = string.Empty;

        public string Version { get; init; } = string.Empty;
    }
}
