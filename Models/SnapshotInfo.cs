namespace SandboxTimeline;

public sealed class SnapshotInfo
{
    public long Id { get; init; }
    public int SnapshotNumber { get; init; }
    public string Label { get; init; } = string.Empty;
    public string VolumePath { get; init; } = @"C:\";
    public string ShadowCopyId { get; init; } = string.Empty;
    public string ShadowDevicePath { get; init; } = string.Empty;
    public string RegistryBackupPath { get; init; } = string.Empty;
    public string ManifestPath { get; init; } = string.Empty;
    public string SnapshotFolder { get; init; } = string.Empty;
    public DateTime CreatedAtUtc { get; init; }
    public long SizeBytes { get; init; }
    public int ChangedFileCount { get; init; }
    public bool UsedUsnJournal { get; init; }
    public bool IsPremiumSnapshot { get; init; }

    public string DisplayLabel =>
        string.IsNullOrWhiteSpace(Label)
            ? $"Snapshot #{SnapshotNumber} ({CreatedAtUtc.ToLocalTime():g})"
            : Label;
}
