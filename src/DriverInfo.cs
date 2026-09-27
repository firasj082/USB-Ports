// The driver a device uses: its name, version, maker, and when its file was
// last updated. The date inside Windows' built-in drivers is always 2006, so
// "updated" comes from the driver file itself, which Windows Update rewrites
// whenever it services that driver.
using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Win32;

static class DriverInfo
{
    static readonly Dictionary<string, KeyValuePair<string, DateTime?>> fileCache = new Dictionary<string, KeyValuePair<string, DateTime?>>(StringComparer.OrdinalIgnoreCase);

    public static void Fill(UsbDevice d, int dev)
    {
        d.DriverService = Native.RegProp(dev, Native.CM_DRP_SERVICE);
        d.DriverName = Native.StringProp(dev, Native.DriverDesc) ?? Native.RegProp(dev, Native.CM_DRP_DEVICEDESC);
        d.DriverProvider = Native.StringProp(dev, Native.DriverProvider);
        d.DriverVersion = Native.StringProp(dev, Native.DriverVersion);
        if (string.IsNullOrEmpty(d.DriverService)) return;

        KeyValuePair<string, DateTime?> file;
        lock (fileCache)
        {
            if (!fileCache.TryGetValue(d.DriverService, out file))
            {
                string path = ServiceFile(d.DriverService);
                DateTime? updated = null;
                try { if (path != null && File.Exists(path)) updated = File.GetLastWriteTime(path); else path = null; }
                catch { path = null; }
                file = new KeyValuePair<string, DateTime?>(path, updated);
                fileCache[d.DriverService] = file;
            }
        }
        d.DriverFile = file.Key;
        d.DriverUpdated = file.Value;
    }

    static string ServiceFile(string service)
    {
        string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        string fallback = Path.Combine(windows, @"System32\drivers\" + service + ".sys");
        try
        {
            using (RegistryKey k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\" + service))
            {
                string p = k == null ? null : k.GetValue("ImagePath") as string;
                if (string.IsNullOrEmpty(p)) return fallback;
                p = Environment.ExpandEnvironmentVariables(p.Trim().Trim('"'));
                if (p.StartsWith(@"\SystemRoot\", StringComparison.OrdinalIgnoreCase)) return Path.Combine(windows, p.Substring(12));
                if (p.StartsWith(@"System32\", StringComparison.OrdinalIgnoreCase)) return Path.Combine(windows, p);
                if (p.StartsWith(@"\??\")) return p.Substring(4);
                return p;
            }
        }
        catch { return fallback; }
    }
}
