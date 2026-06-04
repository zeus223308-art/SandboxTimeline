using System.Diagnostics;
using System.Runtime.InteropServices;
using Alphaleonis.Win32.Vss;

namespace SandboxTimeline;

public sealed class VssManager : IDisposable
{
    private const int MaxStateRetries = 2;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(1);

    private IVssBackupComponents? _backup;
    private Guid _snapshotSetId;
    private string _lastProvider = "none";

    public string LastProviderUsed => _lastProvider;

    public static void Ensure64BitProcess()
    {
        if (!Environment.Is64BitProcess)
        {
            throw new PlatformNotSupportedException(
                "Sandbox Timeline must run as a 64-bit process to use Volume Shadow Copy Service. Rebuild with PlatformTarget=x64.");
        }

        AlphaVssNativeBootstrapper.EnsureInitialized();
    }

    public void CleanupStaleSession()
    {
        if (_backup == null)
        {
            return;
        }

        var backup = _backup;
        _backup = null;

        TryAbortBackup(backup);
        TryBackupComplete(backup);
        TryDisposeBackup(backup);
    }

    public (Guid SnapshotSetId, Guid SnapshotId, string DeviceObject) CreateShadowCopy(
        string volumePath,
        TimeSpan? timeout = null)
    {
        Ensure64BitProcess();
        CleanupStaleSession();

        var normalizedVolume = NormalizeVolumePath(volumePath);
        var maxWait = timeout ?? TimeSpan.FromSeconds(3);

        Exception? lastError = null;
        for (var attempt = 0; attempt <= MaxStateRetries; attempt++)
        {
            if (attempt > 0)
            {
                Thread.Sleep(RetryDelay);
                CleanupStaleSession();
            }

            try
            {
                return CreateShadowCopyAlphaVss(normalizedVolume, maxWait);
            }
            catch (VssBadStateException ex)
            {
                lastError = ex;
                CleanupStaleSession();
                if (attempt >= MaxStateRetries)
                {
                    break;
                }
            }
            catch (Exception ex) when (IsIncorrectState(ex))
            {
                lastError = ex;
                CleanupStaleSession();
                if (attempt >= MaxStateRetries)
                {
                    break;
                }
            }
            catch (COMException ex) when (IsClassNotRegistered(ex))
            {
                CleanupStaleSession();
                var wmiResult = WmiVssShadowProvider.CreateShadowCopy(normalizedVolume);
                _lastProvider = "WMI";
                return (Guid.Empty, wmiResult.SnapshotId, wmiResult.DeviceObject);
            }
        }

        if (lastError != null)
        {
            throw new InvalidOperationException(
                $"AlphaVSS shadow copy failed after {MaxStateRetries + 1} attempt(s): {lastError.Message}",
                lastError);
        }

        throw new InvalidOperationException("AlphaVSS shadow copy failed for an unknown reason.");
    }

    private (Guid SnapshotSetId, Guid SnapshotId, string DeviceObject) CreateShadowCopyAlphaVss(
        string normalizedVolume,
        TimeSpan maxWait)
    {
        IVssBackupComponents? backup = null;
        try
        {
            var factory = VssFactoryProvider.Default.GetVssFactory();
            backup = factory.CreateVssBackupComponents();
            _backup = backup;

            backup.InitializeForBackup(null);
            backup.SetBackupState(false, false, VssBackupType.Full, false);

            backup.GatherWriterMetadata();

            _snapshotSetId = backup.StartSnapshotSet();
            var snapshotId = backup.AddToSnapshotSet(normalizedVolume);
            backup.PrepareForBackup();
            backup.DoSnapshotSet();

            var sw = Stopwatch.StartNew();
            VssSnapshotProperties properties;
            while (true)
            {
                properties = backup.GetSnapshotProperties(snapshotId);
                if (!string.IsNullOrWhiteSpace(properties.SnapshotDeviceObject))
                {
                    break;
                }

                if (sw.Elapsed >= maxWait)
                {
                    throw new TimeoutException(
                        $"VSS shadow copy did not expose a device object within {maxWait.TotalSeconds:F0} seconds.");
                }

                Thread.Sleep(50);
            }

            TryBackupComplete(backup);

            _lastProvider = "AlphaVSS";
            return (_snapshotSetId, snapshotId, properties.SnapshotDeviceObject);
        }
        catch
        {
            if (backup != null)
            {
                TryAbortBackup(backup);
                TryBackupComplete(backup);
            }

            CleanupStaleSession();
            throw;
        }
    }

    public void DeleteSnapshotSet(Guid snapshotSetId)
    {
        if (_backup == null || snapshotSetId == Guid.Empty)
        {
            return;
        }

        try
        {
            _backup.DeleteSnapshotSet(snapshotSetId, forceDelete: true);
        }
        catch
        {
        }
    }

    private static void TryAbortBackup(IVssBackupComponents backup)
    {
        try
        {
            backup.AbortBackup();
        }
        catch
        {
        }
    }

    private static void TryBackupComplete(IVssBackupComponents backup)
    {
        try
        {
            backup.BackupComplete();
        }
        catch
        {
        }
    }

    private static void TryDisposeBackup(IVssBackupComponents backup)
    {
        try
        {
            backup.Dispose();
        }
        catch
        {
        }
    }

    private static string NormalizeVolumePath(string volumePath)
    {
        var root = Path.GetPathRoot(string.IsNullOrWhiteSpace(volumePath) ? @"C:\" : volumePath) ?? @"C:\";
        return root.EndsWith('\\') ? root : root + "\\";
    }

    private static bool IsIncorrectState(Exception ex)
    {
        if (ex is VssBadStateException)
        {
            return true;
        }

        return ex.Message.Contains("incorrect state", StringComparison.OrdinalIgnoreCase)
               || ex.Message.Contains("incorrectly state", StringComparison.OrdinalIgnoreCase)
               || ex.Message.Contains("VSS object was in an incorrect state", StringComparison.OrdinalIgnoreCase)
               || ex.Message.Contains("VssBadState", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsClassNotRegistered(Exception ex)
    {
        if (ex is COMException com && com.HResult == unchecked((int)0x80040154))
        {
            return true;
        }

        return ex.Message.Contains("80040154", StringComparison.OrdinalIgnoreCase)
               || ex.Message.Contains("REGDB_E_CLASSNOTREG", StringComparison.OrdinalIgnoreCase)
               || ex.Message.Contains("클래스가 등록되지 않았습니다", StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        CleanupStaleSession();
    }
}
