namespace SandboxTimeline;

public sealed class SelectiveRestoreResult
{
    public long SnapshotId { get; init; }

    public string TargetFolderPath { get; init; } = string.Empty;

    public int FilesRestored { get; init; }

    public int FilesMissingInShadow { get; init; }

    public int FilesSkipped { get; init; }

    public TimeSpan Elapsed { get; init; }

    public bool Success { get; init; }

    public string Message { get; init; } = string.Empty;
}
