using System.Management;

namespace SandboxTimeline;

/// <summary>
/// WMI-based VSS shadow copy fallback when COM class registration fails.
/// </summary>
public static class WmiVssShadowProvider
{
    public static (Guid SnapshotId, string DeviceObject) CreateShadowCopy(string volumePath)
    {
        var volume = NormalizeVolume(volumePath);
        var options = new InvokeMethodOptions { Timeout = TimeSpan.FromSeconds(30) };

        using var shadowClass = new ManagementClass(@"root\cimv2", "Win32_ShadowCopy", null);
        shadowClass.Get();

        using var inParams = shadowClass.GetMethodParameters("Create");
        inParams["Volume"] = volume;
        inParams["Context"] = "ClientAccessible";

        using var outParams = shadowClass.InvokeMethod("Create", inParams, options);
        var returnValue = Convert.ToUInt32(outParams["ReturnValue"]);
        if (returnValue != 0)
        {
            throw new InvalidOperationException($"WMI Win32_ShadowCopy.Create failed with code {returnValue}.");
        }

        var shadowId = Convert.ToString(outParams["ShadowID"])
            ?? throw new InvalidOperationException("WMI did not return a shadow copy ID.");

        using var shadow = new ManagementObject($"Win32_ShadowCopy.ID='{shadowId}'");
        shadow.Get();

        var deviceObject = Convert.ToString(shadow["DeviceObject"]) ?? string.Empty;
        if (string.IsNullOrWhiteSpace(deviceObject))
        {
            throw new InvalidOperationException("WMI shadow copy has no device object path.");
        }

        if (!Guid.TryParse(shadowId.Trim('{', '}'), out var parsedId))
        {
            parsedId = Guid.NewGuid();
        }

        return (parsedId, deviceObject);
    }

    private static string NormalizeVolume(string volumePath)
    {
        var root = Path.GetPathRoot(string.IsNullOrWhiteSpace(volumePath) ? @"C:\" : volumePath) ?? @"C:\";
        return root.EndsWith('\\') ? root : root + "\\";
    }
}
