namespace SandboxTimeline;

public sealed record StoredLicenseToken
{
    public string ActivationToken { get; init; } = string.Empty;

    public DateTime ExpiresAtUtc { get; init; }

    public bool IsPremium { get; init; }

    /// <summary>
    /// Legacy plaintext field kept only for one-time migration to LicenseKeyHash.
    /// New activations must leave this empty.
    /// </summary>
    public string LicenseKey { get; init; } = string.Empty;

    public string LicenseKeyHash { get; init; } = string.Empty;

    public string ProductMetadataId { get; init; } = ProductionLicenseConfiguration.ProductMetadataId;

    public DateTime LastOnlineValidationUtc { get; init; }

    public DateTime OfflineGraceExpiresAtUtc { get; init; }

    public string? SubscriptionId { get; init; }

    public string? CustomerId { get; init; }

    public DateTime ActivatedAtUtc { get; init; }
}
