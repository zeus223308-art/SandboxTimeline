namespace SandboxTimeline;

internal static class StoreDistribution
{
#if STORE_DISTRIBUTION
    public static bool IsEnabled => true;
#else
    public static bool IsEnabled => false;
#endif

    public static bool IsPackaged =>
        IsEnabled && IsPackagedApp();

    public static bool IsPackagedApp()
    {
        try
        {
            return Windows.ApplicationModel.Package.Current?.Id is not null;
        }
        catch
        {
            return false;
        }
    }
}
