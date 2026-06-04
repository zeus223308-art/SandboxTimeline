using Windows.Services.Store;

namespace SandboxTimeline;

internal sealed class StoreLicenseService
{
    private readonly string _premiumStoreId;
    private StoreContext? _context;

    public StoreLicenseService()
    {
        _premiumStoreId = StoreLicenseConfiguration.ResolvePremiumStoreId();
    }

    public async Task<StoreEntitlementStatus> GetEntitlementAsync(CancellationToken cancellationToken = default)
    {
        if (!StoreDistribution.IsPackaged)
        {
            return StoreEntitlementStatus.NotPremium(
                Loc.Get("Str_StoreInstallRequired"));
        }

        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var license = await GetContext().GetAppLicenseAsync().AsTask().ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            if (license.AddOnLicenses.TryGetValue(_premiumStoreId, out var addOnLicense) &&
                addOnLicense.IsActive)
            {
                var expires = addOnLicense.ExpirationDate.UtcDateTime;
                return StoreEntitlementStatus.Premium(expires, _premiumStoreId);
            }

            return StoreEntitlementStatus.NotPremium();
        }
        catch (Exception ex)
        {
            return StoreEntitlementStatus.NotPremium(ExceptionDisplayFormatter.Format(ex));
        }
    }

    public async Task<StorePurchaseAttemptResult> RequestPurchaseAsync(CancellationToken cancellationToken = default)
    {
        if (!StoreDistribution.IsPackaged)
        {
            return StorePurchaseAttemptResult.OpenListing(
                Loc.Get("Str_StoreInstallRequired"),
                StoreLicenseConfiguration.ResolveStoreListingUri());
        }

        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var purchaseResult = await GetContext()
                .RequestPurchaseAsync(_premiumStoreId)
                .AsTask()
                .ConfigureAwait(false);

            cancellationToken.ThrowIfCancellationRequested();

            return purchaseResult.ExtendedError is not null
                ? StorePurchaseAttemptResult.Failed(ExceptionDisplayFormatter.Format(purchaseResult.ExtendedError))
                : MapPurchaseStatus(purchaseResult.Status);
        }
        catch (Exception ex)
        {
            return StorePurchaseAttemptResult.Failed(ExceptionDisplayFormatter.Format(ex));
        }
    }

    private StoreContext GetContext() => _context ??= StoreContext.GetDefault();

    private static StorePurchaseAttemptResult MapPurchaseStatus(StorePurchaseStatus status) =>
        status switch
        {
            StorePurchaseStatus.Succeeded or StorePurchaseStatus.AlreadyPurchased =>
                StorePurchaseAttemptResult.Succeeded(Loc.Get("Str_StorePurchaseSucceeded")),
            StorePurchaseStatus.NotPurchased =>
                StorePurchaseAttemptResult.Cancelled(Loc.Get("Str_StorePurchaseCancelled")),
            StorePurchaseStatus.NetworkError =>
                StorePurchaseAttemptResult.Failed(Loc.Get("Str_StorePurchaseNetworkError")),
            StorePurchaseStatus.ServerError =>
                StorePurchaseAttemptResult.Failed(Loc.Get("Str_StorePurchaseServerError")),
            _ => StorePurchaseAttemptResult.Failed(Loc.Get("Str_StorePurchaseFailed"))
        };
}

internal readonly record struct StoreEntitlementStatus(
    bool IsPremium,
    DateTime? ExpiresAtUtc,
    string? SubscriptionStoreId,
    string? ErrorMessage)
{
    public static StoreEntitlementStatus Premium(DateTime? expiresAtUtc, string storeId) =>
        new(true, expiresAtUtc, storeId, null);

    public static StoreEntitlementStatus NotPremium(string? errorMessage = null) =>
        new(false, null, null, errorMessage);
}

internal readonly record struct StorePurchaseAttemptResult(
    bool Success,
    bool OpenStoreListing,
    string? StoreListingUri,
    string? UserMessage)
{
    public static StorePurchaseAttemptResult Succeeded(string message) =>
        new(true, false, null, message);

    public static StorePurchaseAttemptResult Cancelled(string message) =>
        new(false, false, null, message);

    public static StorePurchaseAttemptResult Failed(string message) =>
        new(false, false, null, message);

    public static StorePurchaseAttemptResult OpenListing(string message, string listingUri) =>
        new(false, true, listingUri, message);
}
