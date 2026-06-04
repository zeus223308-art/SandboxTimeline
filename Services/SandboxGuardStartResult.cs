namespace SandboxTimeline;

public sealed class SandboxGuardStartResult
{
    public bool Success { get; private init; }

    public string? StatusResourceKey { get; private init; }

    public string? DiagnosticMessage { get; private init; }

    public static SandboxGuardStartResult Succeeded() => new() { Success = true };

    public static SandboxGuardStartResult Blocked(string statusResourceKey) =>
        new()
        {
            Success = false,
            StatusResourceKey = statusResourceKey
        };

    public static SandboxGuardStartResult Failed(string diagnosticMessage) =>
        new()
        {
            Success = false,
            DiagnosticMessage = diagnosticMessage
        };
}
