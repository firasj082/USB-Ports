// Decides what is worth a notification after the USB devices change:
//  - a USB drive that vanished without being safely removed (Windows logs a
//    "surprise removed" event only in that case, so normal ejects stay quiet)
//  - a device that supports USB 3 but connected at USB 2
//  - a port reporting a failure (device failed to start, power surge, ...)
using System;
using System.Collections.Generic;
using System.Diagnostics.Eventing.Reader;

static class UsbAlerts
{
    class Seen { public UsbDevice Device; public int Port; }

    public static List<string> Compare(List<PortReading> before, List<PortReading> after, int ignorePort)
    {
        return Compare(before, after, ignorePort, RemovedUnexpectedly);
    }

    // removedUnexpectedly: did Windows log this device as removed without a safe removal?
    public static List<string> Compare(List<PortReading> before, List<PortReading> after, int ignorePort, Func<string, bool> removedUnexpectedly)
    {
        var messages = new List<string>();
        if (before == null || after == null) return messages;
        Dictionary<string, Seen> was = Flatten(before), now = Flatten(after);

        foreach (KeyValuePair<string, Seen> kv in was)
        {
            UsbDevice d = kv.Value.Device;
            if (kv.Value.Port == ignorePort || now.ContainsKey(kv.Key) || d.DiskInstanceIds.Count == 0) continue;
            if (removedUnexpectedly(kv.Key))
                messages.Add(Label(d) + " disconnected unexpectedly. If you did not unplug it, check its cable and connection; files being copied may be incomplete.");
        }

        foreach (KeyValuePair<string, Seen> kv in now)
        {
            UsbDevice d = kv.Value.Device;
            if (kv.Value.Port == ignorePort || Present.ToneOf(d) != Tone.Warn) continue;
            Seen old;
            if (was.TryGetValue(kv.Key, out old) && Present.ToneOf(old.Device) == Tone.Warn) continue;   // already known
            messages.Add(Label(d) + " connected at USB 2 (480 Mbps) although it supports USB 3. Unplug it and plug it back in firmly for full speed.");
        }

        foreach (PortReading r in after)
        {
            if (r.Problem == null || r.Port.Number == ignorePort) continue;
            PortReading old = before.Find(delegate (PortReading x) { return x.Port.Number == r.Port.Number; });
            if (old != null && old.Problem == r.Problem) continue;
            messages.Add("Port " + r.Port.Number + ": " + r.Problem + ". Try unplugging the device and plugging it back in.");
        }
        return messages;
    }

    static Dictionary<string, Seen> Flatten(List<PortReading> readings)
    {
        var map = new Dictionary<string, Seen>(StringComparer.OrdinalIgnoreCase);
        foreach (PortReading r in readings)
            if (r.Device != null) Add(r.Device, r.Port.Number, map);
        return map;
    }

    static void Add(UsbDevice d, int port, Dictionary<string, Seen> map)
    {
        if (d.InstanceId != null) map[d.InstanceId] = new Seen { Device = d, Port = port };
        foreach (UsbDevice c in d.Children) Add(c, port, map);
    }

    static string Label(UsbDevice d)
    {
        var letters = new List<string>();
        foreach (DriveVolume v in d.Drives) letters.Add(v.Letter);
        return letters.Count > 0 ? d.Name + " (" + string.Join(", ", letters.ToArray()) + ")" : d.Name;
    }

    // Windows logs Kernel-PnP event 1010 only when a device disappears without a safe removal.
    static bool RemovedUnexpectedly(string instanceId)
    {
        try
        {
            var query = new EventLogQuery("Microsoft-Windows-Kernel-PnP/Device Management", PathType.LogName,
                "*[System[(EventID=1010) and TimeCreated[timediff(@SystemTime) <= 120000]]]");
            using (var reader = new EventLogReader(query))
            {
                for (EventRecord e = reader.ReadEvent(); e != null; e = reader.ReadEvent())
                {
                    using (e)
                    {
                        foreach (EventProperty p in e.Properties)
                            if (p.Value is string && string.Equals((string)p.Value, instanceId, StringComparison.OrdinalIgnoreCase)) return true;
                    }
                }
            }
        }
        catch { }
        return false;
    }
}
