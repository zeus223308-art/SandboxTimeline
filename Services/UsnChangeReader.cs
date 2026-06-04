using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace SandboxTimeline;

internal static class UsnChangeReader
{
    private const int MaxRecordsPerRead = 4096;
    private const int BufferSize = 256 * 1024;

    public static List<TrackedFileEntry> ReadChangesSince(long fromUsn, IReadOnlyList<string> watchedRoots)
    {
        var results = new ConcurrentDictionary<string, TrackedFileEntry>(StringComparer.OrdinalIgnoreCase);
        SafeFileHandle? volumeHandle = null;

        try
        {
            volumeHandle = UsnJournalInterop.CreateFile(
                @"\\.\C:",
                0,
                UsnJournalInterop.FileShareRead | UsnJournalInterop.FileShareWrite,
                IntPtr.Zero,
                UsnJournalInterop.OpenExisting,
                UsnJournalInterop.FileFlagBackupSemantics,
                IntPtr.Zero);

            if (volumeHandle.IsInvalid)
            {
                return [];
            }

            if (!UsnJournalInterop.DeviceIoControlQuery(
                    volumeHandle,
                    UsnJournalInterop.FsctlQueryUsnJournal,
                    IntPtr.Zero,
                    0,
                    out UsnJournalInterop.UsnJournalData journal,
                    Marshal.SizeOf<UsnJournalInterop.UsnJournalData>(),
                    out _,
                    IntPtr.Zero))
            {
                return [];
            }

            var lowUsn = fromUsn > 0 ? fromUsn : journal.FirstUsn;
            if (lowUsn >= journal.NextUsn)
            {
                return [];
            }

            var enumData = new UsnJournalInterop.MftEnumData
            {
                StartFileReferenceNumber = 0,
                LowUsn = lowUsn,
                HighUsn = journal.NextUsn
            };

            var buffer = Marshal.AllocHGlobal(BufferSize);
            var recordsRead = 0;
            try
            {
                while (recordsRead < MaxRecordsPerRead &&
                       UsnJournalInterop.DeviceIoControl(
                           volumeHandle,
                           UsnJournalInterop.FsctlEnumUsnData,
                           ref enumData,
                           Marshal.SizeOf<UsnJournalInterop.MftEnumData>(),
                           buffer,
                           BufferSize,
                           out var returned,
                           IntPtr.Zero) && returned > 8)
                {
                    var nextStart = Marshal.ReadInt64(buffer);
                    var offset = 8;
                    while (offset < returned && recordsRead < MaxRecordsPerRead)
                    {
                        var record = Marshal.PtrToStructure<UsnJournalInterop.UsnRecord>(buffer + offset);
                        if (record.RecordLength == 0)
                        {
                            break;
                        }

                        var nameLength = record.FileNameLength;
                        var nameBytes = new byte[nameLength];
                        Marshal.Copy(buffer + offset + record.FileNameOffset, nameBytes, 0, nameLength);
                        var fileName = Encoding.Unicode.GetString(nameBytes);
                        var fullPath = ResolvePathFromUsnName(fileName, watchedRoots);

                        if (!string.IsNullOrEmpty(fullPath) && !SyncExcludedPathFilter.ShouldIgnorePath(fullPath))
                        {
                            if (File.Exists(fullPath))
                            {
                                var entry = TrackedFileMetadata.CreateEntry(fullPath, record.Usn, record.Reason, changed: true);
                                results[entry.RelativePath] = entry;
                            }
                            else if ((record.Reason & UsnJournalInterop.UsnReasonFileDelete) != 0)
                            {
                                var relative = TrackedFileMetadata.ToRelativeProtectedPath(fullPath.Length > 0 ? fullPath : fileName);
                                results[relative] = new TrackedFileEntry
                                {
                                    RelativePath = relative,
                                    FullPath = fullPath.Length > 0 ? fullPath : relative,
                                    ChangeReason = record.Reason,
                                    Usn = record.Usn,
                                    ChangedSincePrevious = true
                                };
                            }
                        }

                        recordsRead++;
                        offset += (int)record.RecordLength;
                    }

                    if (nextStart == enumData.StartFileReferenceNumber)
                    {
                        break;
                    }

                    enumData.StartFileReferenceNumber = nextStart;
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        catch
        {
            return [];
        }
        finally
        {
            volumeHandle?.Dispose();
        }

        return results.Values.ToList();
    }

    public static (long JournalId, long NextUsn) QueryCurrentCheckpoint()
    {
        try
        {
            using var volume = UsnJournalInterop.CreateFile(
                @"\\.\C:",
                0,
                UsnJournalInterop.FileShareRead | UsnJournalInterop.FileShareWrite,
                IntPtr.Zero,
                UsnJournalInterop.OpenExisting,
                UsnJournalInterop.FileFlagBackupSemantics,
                IntPtr.Zero);

            if (volume.IsInvalid)
            {
                return (0, 0);
            }

            if (UsnJournalInterop.DeviceIoControlQuery(
                    volume,
                    UsnJournalInterop.FsctlQueryUsnJournal,
                    IntPtr.Zero,
                    0,
                    out UsnJournalInterop.UsnJournalData journal,
                    Marshal.SizeOf<UsnJournalInterop.UsnJournalData>(),
                    out _,
                    IntPtr.Zero))
            {
                return ((long)journal.UsnJournalId, journal.NextUsn);
            }
        }
        catch
        {
        }

        return (0, 0);
    }

    private static string ResolvePathFromUsnName(string fileName, IReadOnlyList<string> watchedRoots)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return string.Empty;
        }

        var normalizedName = fileName.Replace('/', '\\').TrimStart('\\');
        foreach (var root in watchedRoots)
        {
            var candidate = Path.IsPathRooted(normalizedName)
                ? normalizedName
                : Path.Combine(root, normalizedName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return string.Empty;
    }
}
