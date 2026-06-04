namespace SandboxTimeline;

/// <summary>
/// Defines the maximum offline premium grace window before online vault re-validation is required.
/// </summary>
internal static class OfflineGracePolicy
{
    public static readonly TimeSpan GracePeriod = TimeSpan.FromHours(72);

    public static DateTime ComputeGraceExpiryUtc(DateTime validatedAtUtc) =>
        validatedAtUtc.Add(GracePeriod);

    public static bool IsGraceExpired(DateTime offlineGraceExpiresAtUtc) =>
        offlineGraceExpiresAtUtc <= DateTime.UtcNow;
}
