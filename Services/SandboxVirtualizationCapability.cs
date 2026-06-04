using System.Diagnostics;
using System.Management;

namespace SandboxTimeline;

public sealed class SandboxVirtualizationCapability
{
    public bool IsSupported { get; private init; }

    public string StatusResourceKey { get; private init; } = "Str_SandboxVtDisabled";

    public static SandboxVirtualizationCapability Evaluate()
    {
        var edition = WindowsEditionCapability.Evaluate();
        if (!edition.SupportsSandboxGuard)
        {
            return new SandboxVirtualizationCapability
            {
                IsSupported = false,
                StatusResourceKey = "Str_WindowsHomeSandboxUnsupported"
            };
        }

        if (!HasCpuVirtualizationEnabled())
        {
            return new SandboxVirtualizationCapability
            {
                IsSupported = false,
                StatusResourceKey = "Str_SandboxVtDisabled"
            };
        }

        if (!IsWindowsSandboxInstalled())
        {
            return new SandboxVirtualizationCapability
            {
                IsSupported = false,
                StatusResourceKey = "Str_SandboxFeatureDisabled"
            };
        }

        if (!IsWindowsSandboxFeatureEnabled())
        {
            return new SandboxVirtualizationCapability
            {
                IsSupported = false,
                StatusResourceKey = "Str_SandboxFeatureDisabled"
            };
        }

        return new SandboxVirtualizationCapability { IsSupported = true };
    }

    private static bool HasCpuVirtualizationEnabled()
    {
        if (TryGetVirtualizationFirmwareEnabled(out var firmwareEnabled))
        {
            return firmwareEnabled;
        }

        return TryGetHypervisorPresent();
    }

    private static bool TryGetVirtualizationFirmwareEnabled(out bool enabled)
    {
        enabled = false;

        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT VirtualizationFirmwareEnabled FROM Win32_Processor");

            foreach (ManagementObject processor in searcher.Get().Cast<ManagementObject>())
            {
                using (processor)
                {
                    if (processor["VirtualizationFirmwareEnabled"] is bool firmwareEnabled)
                    {
                        enabled = firmwareEnabled;
                        return true;
                    }
                }
            }
        }
        catch
        {
        }

        return false;
    }

    private static bool TryGetHypervisorPresent()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT HypervisorPresent FROM Win32_ComputerSystem");

            foreach (ManagementObject system in searcher.Get().Cast<ManagementObject>())
            {
                using (system)
                {
                    if (system["HypervisorPresent"] is bool hypervisorPresent)
                    {
                        return hypervisorPresent;
                    }
                }
            }
        }
        catch
        {
        }

        return false;
    }

    private static bool IsWindowsSandboxInstalled()
    {
        var sandboxExe = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            "System32",
            "WindowsSandbox.exe");
        return File.Exists(sandboxExe);
    }

    private static bool IsWindowsSandboxFeatureEnabled()
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "dism.exe",
                Arguments = "/online /get-featureinfo /featurename:Containers-DisposableClientVM",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process == null)
            {
                return false;
            }

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(15000);
            return output.Contains("State : Enabled", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return IsWindowsSandboxInstalled();
        }
    }
}
