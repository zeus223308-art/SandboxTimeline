using System.Diagnostics;
using System.Runtime.InteropServices;

namespace SandboxTimeline;

/// <summary>
/// Restricts process termination to Windows Sandbox host processes explicitly launched by Sandbox Timeline.
/// Host download-folder executables and unrelated system processes are never targeted.
/// </summary>
public static class SandboxProcessKillGuard
{
    private static readonly HashSet<int> RegisteredSandboxHostProcessIds = new();
    private static readonly object RegistryGate = new();

    public static void RegisterSandboxHostProcess(int processId)
    {
        if (processId <= 0)
        {
            return;
        }

        lock (RegistryGate)
        {
            RegisteredSandboxHostProcessIds.Add(processId);
        }
    }

    public static void UnregisterSandboxHostProcess(int processId)
    {
        if (processId <= 0)
        {
            return;
        }

        lock (RegistryGate)
        {
            RegisteredSandboxHostProcessIds.Remove(processId);
        }
    }

    public static bool IsRegisteredSandboxHostProcess(int processId)
    {
        lock (RegistryGate)
        {
            return RegisteredSandboxHostProcessIds.Contains(processId);
        }
    }

    public static bool TryTerminateRegisteredSandboxHostProcess(int processId)
    {
        if (!IsRegisteredSandboxHostProcess(processId))
        {
            return false;
        }

        try
        {
            using var process = Process.GetProcessById(processId);
            if (process.HasExited)
            {
                UnregisterSandboxHostProcess(processId);
                return true;
            }

            if (!string.Equals(
                    process.ProcessName,
                    "WindowsSandbox",
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            process.CloseMainWindow();
            if (!process.WaitForExit(1500))
            {
                process.Kill(entireProcessTree: false);
            }

            UnregisterSandboxHostProcess(processId);
            return true;
        }
        catch
        {
            try
            {
                var processHandle = OpenProcess(ProcessTerminateAccess, false, processId);
                if (processHandle == IntPtr.Zero)
                {
                    return false;
                }

                var terminated = TerminateProcess(processHandle, 0);
                CloseHandle(processHandle);
                if (terminated)
                {
                    UnregisterSandboxHostProcess(processId);
                }

                return terminated;
            }
            catch
            {
                return false;
            }
        }
    }

    public static void TerminateAllRegisteredSandboxHostProcesses()
    {
        int[] snapshot;
        lock (RegistryGate)
        {
            snapshot = RegisteredSandboxHostProcessIds.ToArray();
        }

        foreach (var processId in snapshot)
        {
            TryTerminateRegisteredSandboxHostProcess(processId);
        }
    }

    private const uint ProcessTerminateAccess = 0x0001;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint desiredAccess, bool inheritHandle, int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool TerminateProcess(IntPtr processHandle, uint exitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);
}
