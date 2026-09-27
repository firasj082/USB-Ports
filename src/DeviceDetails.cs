// Everything the Details window shows. Collected only when Details is clicked.
// It reads the device's own USB descriptors (maker, product, serial, power,
// interfaces) through the hub - the same requests Windows makes when the
// device is plugged in - plus what Windows knows about its driver. For drives
// it reads cached disk information; it never reads or writes the drive's data.
using System;
using System.Collections.Generic;
using System.Net.NetworkInformation;
using System.Text;
using Microsoft.Win32.SafeHandles;

public class DetailSection
{
    public string Title;
    public List<KeyValuePair<string, string>> Rows = new List<KeyValuePair<string, string>>();
    public DetailSection(string title) { Title = title; }
    public void Add(string key, string value)
    {
        if (!string.IsNullOrEmpty(value)) Rows.Add(new KeyValuePair<string, string>(key, value.Trim()));
    }
}

static class DeviceDetails
{
    const uint IOCTL_USB_GET_DESCRIPTOR_FROM_NODE_CONNECTION = 0x220410;
    const uint IOCTL_STORAGE_QUERY_PROPERTY = 0x2D1400;
    const uint IOCTL_DISK_GET_DRIVE_GEOMETRY_EX = 0x700A0;

    public static List<DetailSection> Collect(PortReading r)
    {
        var list = new List<DetailSection>();
        UsbDevice d = r.Device;
        if (d == null) return list;
        byte[] dd = d.DeviceDescriptor ?? new byte[18];
        int dev = Native.Locate(d.InstanceId);

        string mfr = null, product = null, serial = null;
        byte[] config = null;
        if (d.HubPath != null)
        {
            using (SafeFileHandle h = Native.OpenHub(d.HubPath))
            {
                if (h != null)
                {
                    int lang = Language(h, d.ConnectionIndex);
                    mfr = StringDescriptor(h, d.ConnectionIndex, dd[14], lang);
                    product = StringDescriptor(h, d.ConnectionIndex, dd[15], lang);
                    serial = StringDescriptor(h, d.ConnectionIndex, dd[16], lang);
                    config = ConfigDescriptor(h, d.ConnectionIndex);
                }
            }
        }

        // ---- device ----
        var g = new DetailSection("Device");
        g.Add("Name", d.Name);
        g.Add("Type", d.Kind);
        if (product != null && product != d.Name) g.Add("Product name (from the device)", product);
        string vendor = Vendors.Name(d.Vid);
        string driverMfg = dev != 0 ? Native.RegProp(dev, Native.CM_DRP_MFG) : null;
        if (driverMfg != null && (driverMfg.StartsWith("(") || driverMfg.StartsWith("Compatible") || driverMfg.StartsWith("Microsoft"))) driverMfg = null;
        g.Add("Manufacturer", mfr ?? vendor ?? driverMfg ?? "Not reported");
        g.Add("Vendor ID", d.Vid.ToString("X4") + (vendor != null ? "  (" + vendor + ")" : "  (not in the built-in list)"));
        g.Add("Product ID", d.Pid.ToString("X4"));
        if (serial == null && d.InstanceId != null)
        {
            string last = d.InstanceId.Substring(d.InstanceId.LastIndexOf('\\') + 1);
            if (last.IndexOf('&') < 0) serial = last.StartsWith("MSFT30") ? last.Substring(6) : last;
        }
        g.Add("Serial number", serial ?? "Not reported");
        g.Add("Device version", Bcd(BitConverter.ToUInt16(dd, 12)));
        list.Add(g);

        // ---- connection ----
        var c = new DetailSection("Connection");
        c.Add("Port", "Port " + r.Port.Number + "  (" + Present.PortKind(r.Port) + ")");
        c.Add("Running at", Present.Generation(d.Speed) + ", " + Present.SpeedName(d.Speed) + " (" + Present.Rate(d.Speed) + ")");
        c.Add("Can run at USB 3", d.Usb3Capable ? "Yes" : "No");
        c.Add("USB version it reports", Bcd(BitConverter.ToUInt16(dd, 2)));
        if (dev != 0)
        {
            c.Add("Connected since", When(Native.DateProp(dev, Native.LastArrivalDate)));
            c.Add("First connected to this PC", When(Native.DateProp(dev, Native.FirstInstallDate)));
        }
        c.Add("USB address", d.Address.ToString());
        c.Add("Open data channels", d.OpenPipes.ToString());
        list.Add(c);

        // ---- power and interfaces (from the configuration descriptor) ----
        if (config != null && config.Length >= 9)
        {
            var p = new DetailSection("Power");
            int unit = d.Speed >= 3 ? 8 : 2;
            int ma = config[8] * unit;
            p.Add("Power it asks for", ma + " mA  (" + (ma * 5 / 1000.0).ToString("0.##") + " W at 5 V)");
            p.Add("Port can supply", d.Speed >= 3 ? "900 mA (USB 3)" : "500 mA (USB 2)");
            p.Add("Power source", (config[7] & 0x40) != 0 ? "Has its own power supply" : "Powered by the USB port");
            p.Add("Can wake the PC", (config[7] & 0x20) != 0 ? "Yes" : "No");
            int supply = d.Speed >= 3 ? 900 : 500;
            if ((config[7] & 0x40) == 0 && ma >= supply * 0.9)
                p.Add("Note", "Its declared maximum is close to the port's limit (" + ma + " of " + supply + " mA). Most of the time it draws less, but if it ever disconnects or slows down, a thin or long cable or an unpowered hub is a likely cause.");
            list.Add(p);

            // An interface can offer alternatives (e.g. a drive: bulk-only or UAS); list them all.
            var names = new SortedDictionary<int, List<string>>();
            var endpoints = new Dictionary<int, int>();
            int i = 0, total = Math.Min(config.Length, BitConverter.ToUInt16(config, 2));
            while (i + 1 < total && config[i] > 0)
            {
                if (config[i + 1] == 4 && i + 8 <= total)
                {
                    int num = config[i + 2];
                    string nm = DeviceKinds.InterfaceName(config[i + 5], config[i + 6], config[i + 7]);
                    if (!names.ContainsKey(num)) { names[num] = new List<string>(); endpoints[num] = 0; }
                    if (!names[num].Contains(nm)) names[num].Add(nm);
                    endpoints[num] = Math.Max(endpoints[num], config[i + 4]);
                }
                i += config[i];
            }
            var ifs = new DetailSection("What it contains (USB interfaces)");
            foreach (KeyValuePair<int, List<string>> kv in names)
                ifs.Add("Interface " + kv.Key, string.Join("  or  ", kv.Value.ToArray()) + "   ·   " + endpoints[kv.Key] + (endpoints[kv.Key] == 1 ? " endpoint" : " endpoints"));
            if (ifs.Rows.Count > 0) list.Add(ifs);
        }
        else if (d.InterfaceClasses.Count > 0)
        {
            var ifs = new DetailSection("What it contains (USB interfaces)");
            int n = 0;
            foreach (string id in d.InterfaceClasses)
            {
                int cls, sub, prot;
                if (DeviceKinds.Parse(id, out cls, out sub, out prot)) ifs.Add("Interface " + n++, DeviceKinds.InterfaceName(cls, sub, prot));
            }
            list.Add(ifs);
        }

        // ---- kind-specific ----
        foreach (string disk in d.DiskInstanceIds) list.Add(StorageSection(disk, d));
        if (d.Kind == "Wi-Fi adapter" || d.Kind == "Network adapter") { DetailSection n = NetworkSection(dev); if (n != null) list.Add(n); }
        if (d.IsHub) list.Add(HubSection(d));

        // ---- driver ----
        if (dev != 0)
        {
            var drv = new DetailSection("Driver");
            int status, problem;
            if (Native.CM_Get_DevNode_Status(out status, out problem, dev, 0) == 0)
                drv.Add("Status", problem == 0 ? "Working normally" : "Problem (code " + problem + ")");
            drv.Add("Windows device class", Native.RegProp(dev, Native.CM_DRP_CLASS));
            drv.Add("Driver name", d.DriverName);
            drv.Add("Driver", d.DriverService);
            drv.Add("Driver provider", d.DriverProvider);
            drv.Add("Driver version", d.DriverVersion);
            if (d.DriverUpdated.HasValue) drv.Add("Driver file last updated", d.DriverUpdated.Value.ToString("d MMM yyyy, h:mm tt"));
            drv.Add("Driver file", d.DriverFile);
            DateTime? date = Native.DateProp(dev, Native.DriverDate);
            if (date.HasValue)
                drv.Add("Driver package date", date.Value.ToLongDateString() +
                    (date.Value.Year == 2006 && date.Value.Month == 6 && date.Value.Day == 21 ? "  (the fixed date on Windows' built-in drivers)" : ""));
            list.Add(drv);

            var ids = new DetailSection("Technical IDs");
            ids.Add("Device instance ID", d.InstanceId);
            ids.Add("Hardware IDs", string.Join("\n", Native.RegMultiProp(dev, Native.CM_DRP_HARDWAREID).ToArray()));
            ids.Add("Location", string.Join("\n", Native.StringListProp(dev, Native.LocationPaths).ToArray()));
            ids.Add("Hub connection", "Port " + d.ConnectionIndex + " of " + Native.PathToInstanceId(d.HubPath));
            list.Add(ids);
        }
        return list;
    }

    // ---------------- storage ----------------

    static DetailSection StorageSection(string diskId, UsbDevice d)
    {
        var s = new DetailSection("Storage");
        string path = null;
        foreach (string p in Native.InterfacePaths(Native.GUID_DEVINTERFACE_DISK, diskId)) { path = p; break; }
        if (path != null)
        {
            using (SafeFileHandle h = Native.CreateFile(path, 0, 3, IntPtr.Zero, 3, 0, IntPtr.Zero))
            {
                if (!h.IsInvalid)
                {
                    byte[] desc = Query(h, 0);
                    if (desc != null)
                    {
                        s.Add("Model", (Ansi(desc, BitConverter.ToInt32(desc, 12)) + " " + Ansi(desc, BitConverter.ToInt32(desc, 16))).Trim());
                        s.Add("Firmware", Ansi(desc, BitConverter.ToInt32(desc, 20)));
                        s.Add("Serial number", Ansi(desc, BitConverter.ToInt32(desc, 24)));
                        s.Add("Removable media", desc[10] != 0 ? "Yes" : "No");
                    }
                    string svc = Native.RegProp(Native.Locate(d.InstanceId), Native.CM_DRP_SERVICE) ?? "";
                    s.Add("USB protocol in use",
                        svc.Equals("UASPStor", StringComparison.OrdinalIgnoreCase) ? "USB Attached SCSI (UAS), the faster protocol" :
                        svc.Equals("USBSTOR", StringComparison.OrdinalIgnoreCase) ? "Bulk-only (BOT), the older protocol" : null);
                    byte[] geo = Native.Ioctl(h, IOCTL_DISK_GET_DRIVE_GEOMETRY_EX, new byte[256]);
                    if (geo != null)
                    {
                        long size = BitConverter.ToInt64(geo, 24);
                        s.Add("Capacity", Present.Size(size) + "  (" + size.ToString("N0") + " bytes)");
                    }
                    byte[] align = Query(h, 6);
                    if (align != null)
                        s.Add("Sector size", BitConverter.ToInt32(align, 16) + " bytes logical, " + BitConverter.ToInt32(align, 20) + " bytes physical");
                    byte[] seek = Query(h, 7);
                    s.Add("Drive type", seek == null ? "Not reported through this connection" : seek[8] != 0 ? "Hard drive (spinning disk)" : "Solid-state (SSD or flash)");
                    byte[] trim = Query(h, 8);
                    s.Add("TRIM", trim == null ? "Not reported through this connection" : trim[8] != 0 ? "Supported" : "Not supported through this connection");
                }
            }
        }
        foreach (DriveVolume v in d.Drives)
        {
            string text = (string.IsNullOrEmpty(v.Label) ? "No label" : v.Label) + (v.FileSystem != null ? "   ·   " + v.FileSystem : "");
            if (v.Total > 0) text += "   ·   " + Present.Size(v.Free) + " free of " + Present.Size(v.Total);
            s.Add("Drive " + v.Letter.TrimEnd(':'), text);
        }
        return s;
    }

    static byte[] Query(SafeFileHandle h, int propertyId)
    {
        byte[] q = new byte[1024];
        BitConverter.GetBytes(propertyId).CopyTo(q, 0);   // QueryType 0 = standard
        byte[] o = Native.Ioctl(h, IOCTL_STORAGE_QUERY_PROPERTY, q);
        return o != null && BitConverter.ToInt32(o, 4) > 0 ? o : null;
    }

    static string Ansi(byte[] b, int offset)
    {
        if (offset <= 0 || offset >= b.Length) return "";
        int end = offset;
        while (end < b.Length && b[end] != 0) end++;
        return Encoding.ASCII.GetString(b, offset, end - offset).Trim();
    }

    // ---------------- network ----------------

    static DetailSection NetworkSection(int dev)
    {
        if (dev == 0) return null;
        var names = new List<string>();
        SubtreeNames(dev, 0, names);
        foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (!names.Contains(ni.Description)) continue;
            var s = new DetailSection("Network");
            s.Add("Adapter", ni.Name);
            s.Add("Status", ni.OperationalStatus == OperationalStatus.Up ? "Connected" : ni.OperationalStatus.ToString());
            if (ni.Speed > 0) s.Add("Link speed", ni.Speed >= 1000000000 ? (ni.Speed / 1e9).ToString("0.#") + " Gbps" : (ni.Speed / 1e6).ToString("0") + " Mbps");
            string mac = ni.GetPhysicalAddress().ToString();
            if (mac.Length == 12) s.Add("MAC address", string.Join(":", new[] { mac.Substring(0, 2), mac.Substring(2, 2), mac.Substring(4, 2), mac.Substring(6, 2), mac.Substring(8, 2), mac.Substring(10, 2) }));
            var ips = new List<string>();
            foreach (UnicastIPAddressInformation a in ni.GetIPProperties().UnicastAddresses) ips.Add(a.Address.ToString());
            s.Add("IP addresses", string.Join("\n", ips.ToArray()));
            return s;
        }
        return null;
    }

    static void SubtreeNames(int dev, int depth, List<string> names)
    {
        string n = Native.RegProp(dev, Native.CM_DRP_FRIENDLYNAME) ?? Native.RegProp(dev, Native.CM_DRP_DEVICEDESC);
        if (n != null) names.Add(n);
        if (depth < 4) foreach (int c in Native.Children(dev)) SubtreeNames(c, depth + 1, names);
    }

    // ---------------- hub ----------------

    static DetailSection HubSection(UsbDevice d)
    {
        var s = new DetailSection("Hub");
        foreach (string path in Native.InterfacePaths(Native.GUID_DEVINTERFACE_USB_HUB, d.InstanceId))
        {
            using (SafeFileHandle h = Native.OpenHub(path))
            {
                if (h == null) continue;
                int type = Native.HubType(h);
                s.Add("Hub type", type == 3 ? "USB 3 hub" : type == 2 ? "USB 2 hub" : "USB hub");
                s.Add("Ports", Native.PortCount(h).ToString());
            }
            break;
        }
        if (d.Children.Count == 0) s.Add("Plugged in", "Nothing");
        foreach (UsbDevice c in d.Children)
            s.Add(c.Name, c.Problem ?? (c.Kind + ", " + Present.Generation(c.Speed) + " (" + Present.Rate(c.Speed) + ")"));
        return s;
    }

    // ---------------- USB descriptors ----------------

    static byte[] Descriptor(SafeFileHandle h, int port, int type, int index, int wIndex, int length)
    {
        byte[] b = new byte[12 + length];
        BitConverter.GetBytes(port).CopyTo(b, 0);
        b[4] = 0x80; b[5] = 6;   // standard GET_DESCRIPTOR (the hub fills these in anyway)
        BitConverter.GetBytes((ushort)((type << 8) | index)).CopyTo(b, 6);
        BitConverter.GetBytes((ushort)wIndex).CopyTo(b, 8);
        BitConverter.GetBytes((ushort)length).CopyTo(b, 10);
        byte[] o = Native.Ioctl(h, IOCTL_USB_GET_DESCRIPTOR_FROM_NODE_CONNECTION, b);
        if (o == null || o[12] < 2) return null;
        int len = Math.Min(o.Length - 12, type == 2 ? BitConverter.ToUInt16(o, 14) : o[12]);
        byte[] data = new byte[len];
        Array.Copy(o, 12, data, 0, len);
        return data;
    }

    static int Language(SafeFileHandle h, int port)
    {
        byte[] langs = Descriptor(h, port, 3, 0, 0, 255);
        return langs != null && langs.Length >= 4 ? BitConverter.ToUInt16(langs, 2) : 0x0409;
    }

    static string StringDescriptor(SafeFileHandle h, int port, int index, int lang)
    {
        if (index == 0) return null;
        byte[] s = Descriptor(h, port, 3, index, lang, 255);
        if (s == null || s.Length < 4) return null;
        string text = Encoding.Unicode.GetString(s, 2, s.Length - 2).Trim('\0', ' ');
        return text.Length == 0 ? null : text;
    }

    static byte[] ConfigDescriptor(SafeFileHandle h, int port)
    {
        return Descriptor(h, port, 2, 0, 0, 4096);
    }

    static string Bcd(ushort v)
    {
        return ((v >> 8) & 0xFF).ToString("X") + "." + (v & 0xFF).ToString("X2");
    }

    static string When(DateTime? t)
    {
        return t.HasValue ? t.Value.ToString("d MMM yyyy, h:mm tt") : null;
    }

    public static string AsText(string title, List<DetailSection> sections)
    {
        var sb = new StringBuilder();
        sb.AppendLine(title);
        foreach (DetailSection s in sections)
        {
            sb.AppendLine();
            sb.AppendLine(s.Title);
            foreach (KeyValuePair<string, string> row in s.Rows)
                sb.AppendLine("  " + row.Key + ": " + row.Value.Replace("\n", "\n    "));
        }
        return sb.ToString();
    }
}
