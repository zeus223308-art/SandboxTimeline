using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;

namespace SandboxTimeline;

public sealed class DifferentialFileTracker
{
    private static readonly string[] DefaultWatchRoots =
    [
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData)
    ];

    private static readonly HashSet<string> SkipDirectoryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "node_modules", ".git", "cache", "Packages", "SandboxTimeline"
    };

    public IReadOnlyList<string> GetWatchRoots()
    {
        return DefaultWatchRoots.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    public DifferentialSnapshotManifest CaptureDelta(
        int snapshotNumber,
        string snapshotFolder,
        string backupRoot,
        DifferentialSnapshotManifest? previousManifest,
        ChangeTrackingCache? changeCache = null)
    {
        Directory.CreateDirectory(snapshotFolder);
        var watchedRoots = GetWatchRoots();
        previousManifest ??= LoadPreviousManifest(backupRoot);
        var previousIndex = BuildPreviousIndex(previousManifest);

        var fromUsn = previousManifest?.UsnCheckpoint ?? 0;
        var usnChanges = UsnChangeReader.ReadChangesSince(fromUsn, watchedRoots);
        var cachedChanges = changeCache?.DrainPendingChanges() ?? [];

        var scanChanges = new List<TrackedFileEntry>();
        if (ShouldRunLightScan(usnChanges, cachedChanges, previousManifest))
        {
            scanChanges = ScanProtectedPathsLight(watchedRoots, previousIndex, maxFiles: 1200, timeBudgetMs: 600);
        }

        var merged = MergeChanges(MergeChanges(usnChanges, cachedChanges), scanChanges)
            .Where(entry => !SyncExcludedPathFilter.ShouldIgnoreTrackedEntry(entry))
            .ToList();
        var currentUsn = UsnChangeReader.QueryCurrentCheckpoint();

        var manifest = new DifferentialSnapshotManifest
        {
            SnapshotNumber = snapshotNumber,
            CreatedAtUtc = DateTime.UtcNow,
            VolumePath = @"C:\",
            UsnJournalId = currentUsn.JournalId,
            UsnCheckpoint = currentUsn.NextUsn,
            UsedUsnJournal = usnChanges.Count > 0 || cachedChanges.Count > 0,
            ChangedFileCount = merged.Count,
            TotalTrackedFileCount = previousIndex.Count + merged.Count,
            ChangedFiles = merged,
            WatchedRoots = watchedRoots.ToList()
        };

        PersistManifest(snapshotFolder, manifest);
        return manifest;
    }

    private static bool ShouldRunLightScan(
        IReadOnlyList<TrackedFileEntry> usnChanges,
        IReadOnlyList<TrackedFileEntry> cachedChanges,
        DifferentialSnapshotManifest? previousManifest)
    {
        if (previousManifest == null)
        {
            return true;
        }

        return usnChanges.Count == 0 && cachedChanges.Count == 0;
    }

    private static Dictionary<string, TrackedFileEntry> BuildPreviousIndex(DifferentialSnapshotManifest? previous)
    {
        var map = new Dictionary<string, TrackedFileEntry>(StringComparer.OrdinalIgnoreCase);
        if (previous == null)
        {
            return map;
        }

        foreach (var file in previous.ChangedFiles)
        {
            map[file.RelativePath] = file;
        }

        return map;
    }

    private static List<TrackedFileEntry> ScanProtectedPathsLight(
        IReadOnlyList<string> watchedRoots,
        IReadOnlyDictionary<string, TrackedFileEntry> previousIndex,
        int maxFiles,
        int timeBudgetMs)
    {
        var changed = new ConcurrentBag<TrackedFileEntry>();
        var deadline = Environment.TickCount64 + timeBudgetMs;
        var scanned = 0;

        foreach (var root in watchedRoots)
        {
            if (Environment.TickCount64 > deadline || scanned >= maxFiles)
            {
                break;
            }

            if (!Directory.Exists(root))
            {
                continue;
            }

            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(root, "*", new EnumerationOptions
                {
                    RecurseSubdirectories = true,
                    IgnoreInaccessible = true,
                    AttributesToSkip = FileAttributes.System | FileAttributes.Device,
                    MaxRecursionDepth = 4
                });
            }
            catch
            {
                continue;
            }

            foreach (var file in files)
            {
                if (Environment.TickCount64 > deadline || scanned >= maxFiles)
                {
                    break;
                }

                if (ShouldSkip(file))
                {
                    continue;
                }

                try
                {
                    var info = new FileInfo(file);
                    if (!info.Exists)
                    {
                        continue;
                    }

                    var entry = TrackedFileMetadata.CreateEntry(file, usn: 0, reason: 0, changed: false);
                    if (!previousIndex.TryGetValue(entry.RelativePath, out var prior) ||
                        !string.Equals(prior.MetadataHash, entry.MetadataHash, StringComparison.Ordinal))
                    {
                        changed.Add(entry with { ChangedSincePrevious = true });
                    }

                    scanned++;
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }

        return changed.ToList();
    }

    private static bool ShouldSkip(string fullPath)
    {
        if (SyncExcludedPathFilter.ShouldIgnorePath(fullPath))
        {
            return true;
        }

        var parts = fullPath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return parts.Any(p => SkipDirectoryNames.Contains(p));
    }

    private static List<TrackedFileEntry> MergeChanges(
        IReadOnlyList<TrackedFileEntry> primary,
        IReadOnlyList<TrackedFileEntry> secondary)
    {
        var map = new Dictionary<string, TrackedFileEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in secondary)
        {
            map[item.RelativePath] = item;
        }

        foreach (var item in primary)
        {
            map[item.RelativePath] = item;
        }

        return map.Values.OrderBy(v => v.RelativePath, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static void PersistManifest(string snapshotFolder, DifferentialSnapshotManifest manifest)
    {
        var manifestPath = Path.Combine(snapshotFolder, "manifest.json");
        var json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(manifestPath, json, Encoding.UTF8);

        var deltaListPath = Path.Combine(snapshotFolder, "changed_files.txt");
        File.WriteAllLines(
            deltaListPath,
            manifest.ChangedFiles.Select(f => $"{f.RelativePath}|{f.SizeBytes}|{f.MetadataHash}"),
            Encoding.UTF8);
    }

    public static DifferentialSnapshotManifest? LoadManifest(string snapshotFolder)
    {
        var path = Path.Combine(snapshotFolder, "manifest.json");
        if (!File.Exists(path))
        {
            return null;
        }

        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<DifferentialSnapshotManifest>(json);
    }

    public static DifferentialSnapshotManifest? LoadPreviousManifest(string backupRoot)
    {
        if (!Directory.Exists(backupRoot))
        {
            return null;
        }

        var latestDir = Directory.GetDirectories(backupRoot)
            .OrderByDescending(Directory.GetCreationTimeUtc)
            .FirstOrDefault();

        return latestDir == null ? null : LoadManifest(latestDir);
    }
}
