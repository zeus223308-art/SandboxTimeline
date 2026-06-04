namespace SandboxTimeline;

/// <summary>
/// Result returned by the remote license vault after hash + product metadata validation.
/// </summary>
public sealed class VaultLicenseValidationResult
{
    public bool IsEntitled { get; private init; }

    public bool IsNetworkError { get; private init; }

    public string? Message { get; private init; }

    public DateTime? SubscriptionExpiresAtUtc { get; private init; }

    public string? SubscriptionId { get; private init; }

    public string? CustomerId { get; private init; }

    public string? ProductMetadataId { get; private init; }

    public static VaultLicenseValidationResult Entitled(
        DateTime? subscriptionExpiresAtUtc,
        string? subscriptionId,
        string? customerId,
        string productMetadataId) =>
        new()
        {
            IsEntitled = true,
            SubscriptionExpiresAtUtc = subscriptionExpiresAtUtc?.ToUniversalTime(),
            SubscriptionId = subscriptionId,
            CustomerId = customerId,
            ProductMetadataId = productMetadataId,
            Message = "Vault confirmed active entitlement."
        };

    public static VaultLicenseValidationResult Revoked(string message) =>
        new()
        {
            IsEntitled = false,
            Message = message
        };

    public static VaultLicenseValidationResult NetworkError(string message) =>
        new()
        {
            IsEntitled = false,
            IsNetworkError = true,
            Message = message
        };

    public static VaultLicenseValidationResult NotConfigured(string message) =>
        new()
        {
            IsEntitled = false,
            Message = message
        };
}
