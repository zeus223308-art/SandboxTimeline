using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;
using Alphaleonis.Win32.Vss;

namespace SandboxTimeline;

/// <summary>
/// Orchestrates VSS shadow copies, differential file tracking, registry backups, and rollback.
/// </summary>
public sealed class SnapshotEngine : IDisposable
{
    private static readonly string[] ProtectedUserPaths =
    [
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
    ];

    private readonly VssManager _vss = new();
    private readonly SnapshotMetadataStore _store = new();
    private readonly DifferentialFileTracker _fileTracker = new();
    private readonly ChangeTrackingCache _changeCache = new();
    private readonly VssStorageManager _storageManager = new();
    private readonly string _backupRoot;
    private readonly object _operationLock = new();

    public SnapshotEngine()
    {
        VssManager.Ensure64BitProcess();
        _backupRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "SandboxTimeline",
            "backups");
        Directory.CreateDirectory(_backupRoot);
        _changeCache.Start();
    }

    public event EventHandler<string>? StatusChanged;
    public event EventHandler<SnapshotInfo>? SnapshotCreated;

    public IReadOnlyList<SnapshotInfo> ListSnapshots() => _store.GetAll();

    public SnapshotInfo CreateSnapshot(string? label = null, bool isPremium = true)
    {
        lock (_operationLock)
        {
            try
            {
                SnapshotPreflightValidator.ValidateOrThrow(_backupRoot);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                RaiseStatus($"Error: {ex.Message}");
                throw;
            }

            EnforceStorageQuota();

            var sw = Stopwatch.StartNew();
            var snapshotNumber = _store.GetNextSnapshotNumber();
            var timestamp = DateTime.UtcNow;
            var snapshotFolder = Path.Combine(_backupRoot, timestamp.ToString("yyyyMMdd_HHmmss_fff"));
            Directory.CreateDirectory(snapshotFolder);

            var volume = @"C:\";
            Guid snapshotId;
            string deviceObject;
            try
            {
                RaiseStatus("Cleaning up previous VSS session...");
                _vss.CleanupStaleSession();

                RaiseStatus("Creating VSS shadow copy (x64)...");
                (_, snapshotId, deviceObject) = CreateShadowCopySafe(volume);
                RaiseStatus($"VSS shadow copy ready via {_vss.LastProviderUsed}.");
            }
            catch (VssBadStateException ex)
            {
                RaiseStatus($"Error: VSS state conflict — {ex.Message}");
                TryCleanupFolder(snapshotFolder);
                throw new InvalidOperationException(
                    "VSS was in an incorrect state. Close other backup tools and try again.",
                    ex);
            }
            catch (COMException ex) when (ex.HResult == unchecked((int)0x80040154))
            {
                RaiseStatus("Error: VSS COM class not registered. Rebuild as x64 and run the 64-bit SandboxTimeline.exe.");
                TryCleanupFolder(snapshotFolder);
                throw new InvalidOperationException(
                    "VSS COM class is not registered (REGDB_E_CLASSNOTREG). Use the x64 build of SandboxTimeline.exe.",
                    ex);
            }
            catch (UnauthorizedAccessException ex)
            {
                RaiseStatus($"Error: {ex.Message}");
                TryCleanupFolder(snapshotFolder);
                throw;
            }
            catch (IOException ex)
            {
                RaiseStatus($"Error: {ex.Message}");
                TryCleanupFolder(snapshotFolder);
                throw;
            }
            catch (Exception ex)
            {
                RaiseStatus($"Error: VSS failed — {ex.Message}");
                TryCleanupFolder(snapshotFolder);
                throw new InvalidOperationException($"Could not create a volume shadow copy: {ex.Message}", ex);
            }

            try
            {
                RaiseStatus("Exporting registry hives...");
                var registryPath = Path.Combine(snapshotFolder, "registry");
                ExportRegistryHives(registryPath);

                RaiseStatus("Capturing differential delta (USN journal + RAM cache)...");
                var previousManifest = DifferentialFileTracker.LoadPreviousManifest(_backupRoot);
                var manifest = _fileTracker.CaptureDelta(
                    snapshotNumber,
                    snapshotFolder,
                    _backupRoot,
                    previousManifest,
                    _changeCache);
                var manifestPath = Path.Combine(snapshotFolder, "manifest.json");

                var displayLabel = label ?? $"Snapshot #{snapshotNumber} ({timestamp.ToLocalTime():g})";
                var totalSize = EstimateSnapshotFolderSize(snapshotFolder);

                var info = new SnapshotInfo
                {
                    SnapshotNumber = snapshotNumber,
                    Label = displayLabel,
                    VolumePath = volume,
                    ShadowCopyId = snapshotId.ToString("B"),
                    ShadowDevicePath = deviceObject,
                    RegistryBackupPath = registryPath,
                    ManifestPath = manifestPath,
                    SnapshotFolder = snapshotFolder,
                    CreatedAtUtc = timestamp,
                    SizeBytes = totalSize,
                    ChangedFileCount = manifest.ChangedFileCount,
                    UsedUsnJournal = manifest.UsedUsnJournal,
                    IsPremiumSnapshot = isPremium
                };

                var id = _store.Insert(info);
                sw.Stop();

                var result = new SnapshotInfo
                {
                    Id = id,
                    SnapshotNumber = info.SnapshotNumber,
                    Label = info.Label,
                    VolumePath = info.VolumePath,
                    ShadowCopyId = info.ShadowCopyId,
                    ShadowDevicePath = info.ShadowDevicePath,
                    RegistryBackupPath = info.RegistryBackupPath,
                    ManifestPath = info.ManifestPath,
                    SnapshotFolder = info.SnapshotFolder,
                    CreatedAtUtc = info.CreatedAtUtc,
                    SizeBytes = info.SizeBytes,
                    ChangedFileCount = info.ChangedFileCount,
                    UsedUsnJournal = info.UsedUsnJournal,
                    IsPremiumSnapshot = info.IsPremiumSnapshot
                };

                var trackingMode = manifest.UsedUsnJournal ? "USN journal + RAM cache" : "light metadata scan";
                RaiseStatus(
                    $"Snapshot #{snapshotNumber} created in {sw.ElapsedMilliseconds} ms — {manifest.ChangedFileCount} changed file(s) via {trackingMode}.");
                SnapshotCreated?.Invoke(this, result);
                return result;
            }
            catch (Exception ex)
            {
                RaiseStatus($"Error: Snapshot failed — {ex.Message}");
                TryCleanupFolder(snapshotFolder);
                throw;
            }
        }
    }

    private (Guid SnapshotSetId, Guid SnapshotId, string DeviceObject) CreateShadowCopySafe(string volume)
    {
        return _vss.CreateShadowCopy(volume, TimeSpan.FromSeconds(3));
    }

    public void RollbackToSnapshot(long snapshotId)
    {
        lock (_operationLock)
        {
            if (!SnapshotPreflightValidator.IsRunningAsAdministrator())
            {
                throw new UnauthorizedAccessException("Administrator privileges are required for rollback.");
            }

            var snapshot = _store.GetById(snapshotId)
                ?? throw new InvalidOperationException($"Snapshot {snapshotId} was not found.");

            RaiseStatus("Restoring registry hives...");
            RestoreRegistryHives(snapshot.RegistryBackupPath);

            RaiseStatus("Restoring tracked files from VSS shadow copy...");
            RestoreTrackedFilesFromShadow(snapshot);

            RaiseStatus($"Rollback to {snapshot.DisplayLabel} completed.");
        }
    }

    public void RollbackToLatest()
    {
        var latest = _store.GetLatest()
            ?? throw new InvalidOperationException("No snapshots are available for rollback.");
        RollbackToSnapshot(latest.Id);
    }

    /// <summary>
    /// Restores only files under <paramref name="targetFolderPath"/> from the snapshot's VSS shadow copy.
    /// Does not roll back registry or other folders.
    /// </summary>
    public SelectiveRestoreResult RestoreFolderFromSnapshot(long snapshotId, string targetFolderPath)
    {
        lock (_operationLock)
        {
            if (!SnapshotPreflightValidator.IsRunningAsAdministrator())
            {
                throw new UnauthorizedAccessException("Administrator privileges are required for selective restore.");
            }

            if (string.IsNullOrWhiteSpace(targetFolderPath))
            {
                throw new ArgumentException("Target folder path is required.", nameof(targetFolderPath));
            }

            var targetFolder = Path.GetFullPath(targetFolderPath).TrimEnd('\\');
            if (!Directory.Exists(targetFolder))
            {
                throw new DirectoryNotFoundException($"Target folder does not exist: {targetFolder}");
            }

            var snapshot = _store.GetById(snapshotId)
                ?? throw new InvalidOperationException($"Snapshot {snapshotId} was not found.");

            if (string.IsNullOrWhiteSpace(snapshot.ShadowDevicePath))
            {
                throw new InvalidOperationException("Snapshot has no VSS shadow device path.");
            }

            var sw = Stopwatch.StartNew();
            var restored = 0;
            var missingInShadow = 0;
            var skipped = 0;

            RaiseStatus($"Selective restore: {targetFolder} from {snapshot.DisplayLabel}...");

            var manifest = DifferentialFileTracker.LoadManifest(snapshot.SnapshotFolder);
            var manifestPaths = manifest?.ChangedFiles
                .Where(f => IsUnderFolder(f.FullPath, targetFolder))
                .Where(f => !SyncExcludedPathFilter.ShouldIgnoreTrackedEntry(f))
                .Select(f => f.FullPath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList() ?? [];

            if (manifestPaths.Count > 0)
            {
                foreach (var filePath in manifestPaths)
                {
                    if (TryRestoreFileFromShadow(snapshot.ShadowDevicePath, filePath, out var outcome))
                    {
                        if (outcome == RestoreOutcome.Restored)
                        {
                            restored++;
                        }
                        else if (outcome == RestoreOutcome.MissingInShadow)
                        {
                            missingInShadow++;
                        }
                        else
                        {
                            skipped++;
                        }
                    }
                }
            }
            else
            {
                var shadowFolder = MapLivePathToShadow(snapshot.ShadowDevicePath, targetFolder);
                if (!Directory.Exists(shadowFolder))
                {
                    sw.Stop();
                    return new SelectiveRestoreResult
                    {
                        SnapshotId = snapshotId,
                        TargetFolderPath = targetFolder,
                        FilesRestored = 0,
                        FilesMissingInShadow = 0,
                        FilesSkipped = 0,
                        Elapsed = sw.Elapsed,
                        Success = false,
                        Message = "Folder was not present in the shadow copy."
                    };
                }

                foreach (var shadowFile in Directory.EnumerateFiles(shadowFolder, "*", SearchOption.AllDirectories))
                {
                    var relative = Path.GetRelativePath(shadowFolder, shadowFile);
                    var livePath = Path.Combine(targetFolder, relative);
                    if (SyncExcludedPathFilter.ShouldIgnorePath(livePath))
                    {
                        skipped++;
                        continue;
                    }

                    if (TryRestoreFileFromShadow(snapshot.ShadowDevicePath, livePath, out var outcome))
                    {
                        if (outcome == RestoreOutcome.Restored)
                        {
                            restored++;
                        }
                        else if (outcome == RestoreOutcome.MissingInShadow)
                        {
                            missingInShadow++;
                        }
                        else
                        {
                            skipped++;
                        }
                    }
                }
            }

            sw.Stop();
            var message =
                $"Restored {restored} file(s) to {targetFolder} in {sw.ElapsedMilliseconds} ms " +
                $"({missingInShadow} missing in shadow, {skipped} skipped).";
            RaiseStatus(message);

            return new SelectiveRestoreResult
            {
                SnapshotId = snapshotId,
                TargetFolderPath = targetFolder,
                FilesRestored = restored,
                FilesMissingInShadow = missingInShadow,
                FilesSkipped = skipped,
                Elapsed = sw.Elapsed,
                Success = restored > 0 || (missingInShadow == 0 && skipped == 0),
                Message = message
            };
        }
    }

    private enum RestoreOutcome
    {
        Restored,
        MissingInShadow,
        Skipped
    }

    private static bool IsUnderFolder(string filePath, string folderPath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return false;
        }

        var full = Path.GetFullPath(filePath).TrimEnd('\\');
        return full.StartsWith(folderPath + "\\", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(full, folderPath, StringComparison.OrdinalIgnoreCase);
    }

    private static string MapLivePathToShadow(string shadowDeviceObject, string livePath)
    {
        var relative = livePath.Substring(Path.GetPathRoot(livePath)?.Length ?? 0).TrimStart('\\');
        return Path.Combine(shadowDeviceObject.TrimEnd('\\'), relative);
    }

    private static bool TryRestoreFileFromShadow(string shadowDeviceObject, string targetFile, out RestoreOutcome outcome)
    {
        outcome = RestoreOutcome.Skipped;
        if (string.IsNullOrWhiteSpace(targetFile) || targetFile.Length < 3)
        {
            return false;
        }

        if (SyncExcludedPathFilter.ShouldIgnorePath(targetFile))
        {
            outcome = RestoreOutcome.Skipped;
            return false;
        }

        var shadowSource = MapLivePathToShadow(shadowDeviceObject, targetFile);
        if (!File.Exists(shadowSource))
        {
            outcome = RestoreOutcome.MissingInShadow;
            return true;
        }

        if (File.Exists(targetFile))
        {
            try
            {
                var liveInfo = new FileInfo(targetFile);
                var shadowInfo = new FileInfo(shadowSource);
                if (liveInfo.Length == shadowInfo.Length &&
                    liveInfo.LastWriteTimeUtc == shadowInfo.LastWriteTimeUtc)
                {
                    outcome = RestoreOutcome.Skipped;
                    return true;
                }
            }
            catch
            {
            }
        }

        var dir = Path.GetDirectoryName(targetFile);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        try
        {
            File.Copy(shadowSource, targetFile, overwrite: true);
            outcome = RestoreOutcome.Restored;
            return true;
        }
        catch (IOException)
        {
            outcome = RestoreOutcome.Skipped;
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            outcome = RestoreOutcome.Skipped;
            return true;
        }
    }

    public void PruneOldSnapshots(int keepCount, LicenseManager license)
    {
        var limit = license.IsPremium ? keepCount : license.GetMaxFreeSnapshots();
        var all = _store.GetAll();
        if (all.Count > limit)
        {
            var toRemove = all.Skip(limit).ToList();
            foreach (var snap in toRemove)
            {
                DeleteSnapshot(snap);
            }
        }

        EnforceStorageQuota();
    }

    private void EnforceStorageQuota()
    {
        var removed = _storageManager.EnforceQuota(_backupRoot, _store.GetAll(), DeleteSnapshot);
        if (removed > 0)
        {
            RaiseStatus(Loc.Format("Str_StorageQuotaReclaimedFormat", removed));
        }
    }

    private void DeleteSnapshot(SnapshotInfo snap)
    {
        try
        {
            if (Guid.TryParse(snap.ShadowCopyId, out var shadowId))
            {
                DeleteWmiShadowCopy(shadowId);
            }
        }
        catch
        {
        }

        if (!string.IsNullOrEmpty(snap.SnapshotFolder) && Directory.Exists(snap.SnapshotFolder))
        {
            TryCleanupFolder(snap.SnapshotFolder);
        }
        else if (Directory.Exists(Path.GetDirectoryName(snap.RegistryBackupPath)!))
        {
            TryCleanupFolder(Path.GetDirectoryName(snap.RegistryBackupPath)!);
        }

        _store.Delete(snap.Id);
    }

    private void RestoreTrackedFilesFromShadow(SnapshotInfo snapshot)
    {
        var manifest = DifferentialFileTracker.LoadManifest(snapshot.SnapshotFolder);
        if (manifest != null && manifest.ChangedFiles.Count > 0)
        {
            foreach (var file in manifest.ChangedFiles)
            {
                if (SyncExcludedPathFilter.ShouldIgnoreTrackedEntry(file))
                {
                    continue;
                }

                RestoreSingleFileFromShadow(snapshot.ShadowDevicePath, file.FullPath);
            }

            return;
        }

        RestoreUserFilesFromShadow(snapshot.ShadowDevicePath);
    }

    private static void RestoreSingleFileFromShadow(string shadowDeviceObject, string targetFile)
    {
        if (string.IsNullOrWhiteSpace(targetFile) || targetFile.Length < 3)
        {
            return;
        }

        if (SyncExcludedPathFilter.ShouldIgnorePath(targetFile))
        {
            return;
        }

        var relative = targetFile.Substring(Path.GetPathRoot(targetFile)?.Length ?? 0).TrimStart('\\');
        var shadowSource = Path.Combine(shadowDeviceObject.TrimEnd('\\'), relative);
        if (!File.Exists(shadowSource))
        {
            return;
        }

        var dir = Path.GetDirectoryName(targetFile);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        try
        {
            File.Copy(shadowSource, targetFile, overwrite: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private void RestoreUserFilesFromShadow(string shadowDeviceObject)
    {
        foreach (var userPath in ProtectedUserPaths)
        {
            if (!Directory.Exists(userPath))
            {
                continue;
            }

            var relative = userPath.TrimEnd('\\');
            if (relative.Length < 3)
            {
                continue;
            }

            var shadowSource = Path.Combine(
                shadowDeviceObject.TrimEnd('\\'),
                relative.Substring(3));

            if (!Directory.Exists(shadowSource))
            {
                continue;
            }

            MirrorDirectory(shadowSource, userPath);
        }
    }

    private static void MirrorDirectory(string sourceRoot, string targetRoot)
    {
        foreach (var file in Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceRoot, file);
            var targetFile = Path.Combine(targetRoot, relative);
            if (SyncExcludedPathFilter.ShouldIgnorePath(targetFile))
            {
                continue;
            }

            var targetDir = Path.GetDirectoryName(targetFile);
            if (!string.IsNullOrEmpty(targetDir))
            {
                Directory.CreateDirectory(targetDir);
            }

            try
            {
                File.Copy(file, targetFile, overwrite: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private static void ExportRegistryHives(string folder)
    {
        Directory.CreateDirectory(folder);
        ExportHive(@"HKCU\Software", Path.Combine(folder, "hkcu_software.reg"));
        ExportHive(@"HKCU\Environment", Path.Combine(folder, "hkcu_environment.reg"));
        ExportHive(@"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", Path.Combine(folder, "uninstall.reg"));
    }

    private static void RestoreRegistryHives(string folder)
    {
        ImportHive(Path.Combine(folder, "hkcu_software.reg"));
        ImportHive(Path.Combine(folder, "hkcu_environment.reg"));
        ImportHive(Path.Combine(folder, "uninstall.reg"));
    }

    private static void ExportHive(string key, string outputFile)
    {
        RunProcess("reg.exe", $"export \"{key}\" \"{outputFile}\" /y");
    }

    private static void ImportHive(string regFile)
    {
        if (!File.Exists(regFile))
        {
            return;
        }

        RunProcess("reg.exe", $"import \"{regFile}\"");
    }

    private static void RunProcess(string fileName, string arguments)
    {
        using var process = Process.Start(new ProcessStartInfo(fileName, arguments)
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        });

        process?.WaitForExit(120_000);
    }

    private static long EstimateSnapshotFolderSize(string folder)
    {
        if (!Directory.Exists(folder))
        {
            return 0;
        }

        return Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
            .Sum(f =>
            {
                try
                {
                    return new FileInfo(f).Length;
                }
                catch
                {
                    return 0L;
                }
            });
    }

    private static void TryCleanupFolder(string folder)
    {
        try
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, true);
            }
        }
        catch
        {
        }
    }

    private static void DeleteWmiShadowCopy(Guid shadowId)
    {
        var query = $"SELECT * FROM Win32_ShadowCopy WHERE ID='{{{shadowId}}}'";
        using var searcher = new ManagementObjectSearcher("root\\cimv2", query);
        foreach (ManagementObject obj in searcher.Get())
        {
            obj.Delete();
            obj.Dispose();
        }
    }

    private void RaiseStatus(string message) => StatusChanged?.Invoke(this, message);

    public void Shutdown()
    {
        lock (_operationLock)
        {
            _vss.CleanupStaleSession();
        }
    }

    public void Dispose()
    {
        Shutdown();
        _changeCache.Dispose();
        _vss.Dispose();
        _store.Dispose();
    }
}
