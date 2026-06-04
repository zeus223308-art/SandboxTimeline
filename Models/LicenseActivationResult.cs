namespace SandboxTimeline;

public sealed class LicenseActivationResult
{
    public bool Success { get; init; }

    public bool IsPremium { get; init; }

    public string StatusMessage { get; init; } = string.Empty;

    public bool IsNetworkError { get; init; }

    public string? DiagnosticDetails { get; init; }

    public static LicenseActivationResult Succeeded(string statusMessage) =>
        new() { Success = true, IsPremium = true, StatusMessage = statusMessage };

    public static LicenseActivationResult Failed(
        string statusMessage,
        bool isNetworkError = false,
        string? diagnosticDetails = null) =>
        new()
        {
            Success = false,
            IsPremium = false,
            StatusMessage = statusMessage,
            IsNetworkError = isNetworkError,
            DiagnosticDetails = diagnosticDetails
        };
}
