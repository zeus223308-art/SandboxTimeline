namespace SandboxTimeline;

internal static class StoreLicenseConfiguration
{
    /// <summary>
    /// Partner Center → Add-on / subscription Store ID. Replace after creating the $3.99 SKU.
    /// </summary>
    public const string DefaultPremiumStoreId = "PremiumMonthly";

    public const string PremiumStoreIdEnvironmentVariable = "SANDBOXTIMELINE_STORE_PREMIUM_ID";

    /// <summary>
    /// Microsoft Store product page. Set after Partner Center assigns ProductId (9N...).
    /// </summary>
    public const string DefaultStoreListingUri = "https://apps.microsoft.com/store/detail/sandbox-timeline";

    public const string StoreListingUriEnvironmentVariable = "SANDBOXTIMELINE_STORE_LISTING_URI";

    public static string ResolvePremiumStoreId()
    {
        var fromEnvironment = Environment.GetEnvironmentVariable(PremiumStoreIdEnvironmentVariable)?.Trim();
        return string.IsNullOrWhiteSpace(fromEnvironment)
            ? DefaultPremiumStoreId
            : fromEnvironment;
    }

    public static string ResolveStoreListingUri()
    {
        var fromEnvironment = Environment.GetEnvironmentVariable(StoreListingUriEnvironmentVariable)?.Trim();
        return string.IsNullOrWhiteSpace(fromEnvironment)
            ? DefaultStoreListingUri
            : fromEnvironment;
    }
}
