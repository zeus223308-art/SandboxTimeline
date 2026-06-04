namespace SandboxTimeline;

public sealed class DifferentialSnapshotManifest
{
    public int SnapshotNumber { get; init; }
    public DateTime CreatedAtUtc { get; init; }
    public string VolumePath { get; init; } = @"C:\";
    public long UsnJournalId { get; init; }
    public long UsnCheckpoint { get; init; }
    public bool UsedUsnJournal { get; init; }
    public int ChangedFileCount { get; init; }
    public int TotalTrackedFileCount { get; init; }
    public List<TrackedFileEntry> ChangedFiles { get; init; } = new();
    public List<string> WatchedRoots { get; init; } = new();
}
