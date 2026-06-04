namespace SandboxTimeline;

public sealed record TrackedFileEntry
{
    public string RelativePath { get; init; } = string.Empty;
    public string FullPath { get; init; } = string.Empty;
    public long SizeBytes { get; init; }
    public long LastWriteUtcTicks { get; init; }
    public string MetadataHash { get; init; } = string.Empty;
    public long Usn { get; init; }
    public uint ChangeReason { get; init; }
    public bool ChangedSincePrevious { get; init; }
}
