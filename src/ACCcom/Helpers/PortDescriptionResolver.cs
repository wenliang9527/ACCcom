using System;
using System.Collections.Generic;
using Microsoft.Win32;

namespace ACCcom.Helpers;

/// <summary>
/// Maps COM port names to friendly device descriptions (e.g. "COM3" →
/// "USB-SERIAL CH340") by scanning the Windows registry device tree at
/// HKLM\SYSTEM\CurrentControlSet\Enum for the instance whose
/// "Device Parameters\PortName" matches, then reading its FriendlyName.
/// Results are cached per port name (the device tree only changes when
/// hardware is plugged/unplugged, which the port monitor already tracks).
/// </summary>
public static class PortDescriptionResolver
{
    private static readonly Dictionary<string, string> Cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Friendly device description for a COM port, or empty when unknown.</summary>
    public static string Describe(string portName)
    {
        if (string.IsNullOrEmpty(portName)) return "";
        if (Cache.TryGetValue(portName, out var cached)) return cached;

        var desc = Lookup(portName);
        Cache[portName] = desc;
        return desc;
    }

    /// <summary>Forget cached descriptions (call after plug/unplug events).</summary>
    public static void Invalidate() => Cache.Clear();

    private static string Lookup(string portName)
    {
        try
        {
            using var enumKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum");
            if (enumKey == null) return "";

            foreach (var bus in enumKey.GetSubKeyNames())
            {
                using var busKey = enumKey.OpenSubKey(bus);
                if (busKey == null) continue;

                foreach (var device in busKey.GetSubKeyNames())
                {
                    using var deviceKey = busKey.OpenSubKey(device);
                    if (deviceKey == null) continue;

                    foreach (var instance in deviceKey.GetSubKeyNames())
                    {
                        using var instanceKey = deviceKey.OpenSubKey(instance);
                        if (instanceKey == null) continue;

                        string? found = null;
                        try
                        {
                            using var deviceParams = instanceKey.OpenSubKey("Device Parameters");
                            var pn = deviceParams?.GetValue("PortName") as string;
                            if (string.IsNullOrEmpty(pn) ||
                                !string.Equals(pn, portName, StringComparison.OrdinalIgnoreCase))
                                continue;
                            found = instanceKey.GetValue("FriendlyName") as string
                                    ?? deviceKey.GetValue("FriendlyName") as string;
                        }
                        catch
                        {
                            // some nodes cannot be opened; skip them
                        }
                        if (found == null) continue;
                        return Clean(found);
                    }
                }
            }
        }
        catch
        {
            // registry unavailable — fall through to empty description
        }
        return "";
    }

    /// <summary>"USB-SERIAL CH340 (COM3)" → "USB-SERIAL CH340".</summary>
    private static string Clean(string friendly)
    {
        var idx = friendly.LastIndexOf('(');
        if (idx > 0) friendly = friendly[..idx].TrimEnd();
        return friendly.Trim();
    }
}
