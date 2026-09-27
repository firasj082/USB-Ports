// Reads the laptop's USB sockets straight from the USB hub drivers (the same
// IOCTLs Microsoft's USBView uses). Nothing here talks to the plugged-in
// devices themselves, so it is safe to call while a drive is busy.
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

// One socket on the laptop. A USB 3 socket is two ports inside Windows: a
// USB 2 half and a USB 3 half. The firmware says which halves pair up.
public class PhysicalPort
{
    public int Number;          // 1, 2, 3 ... in the firmware's order
    public string HubPath;
    public string HubInstanceId;
    public int Usb2Port;        // 0 if the socket has no USB 2 half
    public int Usb3Port;        // 0 if the socket has no USB 3 half
    public bool IsTypeC;
}

public class DriveVolume
{
    public string Letter;       // "D:"
    public string Label;
    public string FileSystem;
    public long Total;
    public long Free;
}

public class UsbDevice
{
    public string Name;
    public string Kind;
    public int Speed;           // 0 low, 1 full, 2 high, 3 SuperSpeed (5 Gbps), 4 SuperSpeed+ (10 Gbps)
    public bool Usb3Capable;
    public bool IsHub;
    public string Problem;      // set for a failed device found behind a hub
    public List<DriveVolume> Drives = new List<DriveVolume>();
    public List<UsbDevice> Children = new List<UsbDevice>();   // devices plugged into a hub

    // Where it is and what it is, for Details and Reconnect.
    public string HubPath;
    public int ConnectionIndex;
    public string InstanceId;
    public byte[] DeviceDescriptor;   // the 18-byte USB device descriptor the hub cached
    public int Address;
    public int OpenPipes;
    public List<string> DiskInstanceIds = new List<string>();
    public List<string> InterfaceClasses = new List<string>();   // "USB\Class_03&SubClass_01&Prot_02", ...

    // Driver (see DriverInfo)
    public string DriverName, DriverService, DriverProvider, DriverVersion, DriverFile;
    public DateTime? DriverUpdated;

    public ushort Vid { get { return DeviceDescriptor == null ? (ushort)0 : BitConverter.ToUInt16(DeviceDescriptor, 8); } }
    public ushort Pid { get { return DeviceDescriptor == null ? (ushort)0 : BitConverter.ToUInt16(DeviceDescriptor, 10); } }
}

public class PortReading
{
    public PhysicalPort Port;
    public UsbDevice Device;    // null when nothing is plugged in
    public string Problem;      // set when the socket reports a failure
}

public static class UsbScanner
{
    const uint IOCTL_USB_GET_PORT_CONNECTOR_PROPERTIES = 0x220458;
    const uint IOCTL_USB_GET_NODE_CONNECTION_INFORMATION_EX = 0x220448;
    const uint IOCTL_STORAGE_GET_DEVICE_NUMBER = 0x2D1080;
    const int MaxHubDepth = 3;

    // ---------------- layout (once) ----------------

    public static List<PhysicalPort> FindPhysicalPorts()
    {
        var sockets = new List<PhysicalPort>();
        foreach (string path in Native.InterfacePaths(Native.GUID_DEVINTERFACE_USB_HUB, null))
        {
            if (path.IndexOf("ROOT_HUB", StringComparison.OrdinalIgnoreCase) < 0) continue;
            using (SafeFileHandle h = Native.OpenHub(path))
            {
                if (h == null) continue;
                int count = Native.PortCount(h);
                var claimed = new HashSet<int>();
                for (int n = 1; n <= count; n++)
                {
                    if (claimed.Contains(n)) continue;
                    byte[] pc = new byte[512]; BitConverter.GetBytes(n).CopyTo(pc, 0);
                    pc = Native.Ioctl(h, IOCTL_USB_GET_PORT_CONNECTOR_PROPERTIES, pc);
                    if (pc == null || (BitConverter.ToUInt32(pc, 8) & 1) == 0) continue;   // not a user-reachable socket
                    int companion = BitConverter.ToUInt16(pc, 14);
                    var s = new PhysicalPort { HubPath = path, HubInstanceId = Native.PathToInstanceId(path), IsTypeC = (BitConverter.ToUInt32(pc, 8) & 8) != 0 };
                    byte[] v2 = Native.ConnectionV2(h, n);
                    if (v2 != null && (BitConverter.ToUInt32(v2, 8) & 4) != 0) { s.Usb3Port = n; s.Usb2Port = companion; }
                    else { s.Usb2Port = n; s.Usb3Port = companion; }
                    claimed.Add(n);
                    if (companion > 0) claimed.Add(companion);
                    sockets.Add(s);
                }
            }
        }
        sockets.Sort(delegate (PhysicalPort a, PhysicalPort b) {
            int c = string.Compare(a.HubPath, b.HubPath, StringComparison.Ordinal);
            return c != 0 ? c : SortKey(a).CompareTo(SortKey(b));
        });
        for (int i = 0; i < sockets.Count; i++) sockets[i].Number = i + 1;
        return sockets;
    }

    static int SortKey(PhysicalPort p) { return p.Usb2Port > 0 ? p.Usb2Port : 1000 + p.Usb3Port; }

    // ---------------- status (polled) ----------------

    // Quick read: devices, speeds and drive letters only - no label / size /
    // free-space or driver lookups. Used at shutdown (so a stuck drive cannot
    // hold it up) and for background scans while no window shows those details.
    [ThreadStatic] static bool lettersOnly;

    // Shutdown: quick, and always looks afresh so it ejects exactly what is attached now.
    public static PortReading ReadForShutdown(PhysicalPort s)
    {
        lettersOnly = true;
        try { return Read(s); }
        finally { lettersOnly = false; }
    }

    // Background scans (tray, corner mode): quick, and reuses what devices are.
    public static PortReading ReadQuick(PhysicalPort s)
    {
        lettersOnly = true;
        reuseIdentity = true;
        try { return Read(s); }
        finally { lettersOnly = false; reuseIdentity = false; }
    }

    public static PortReading Read(PhysicalPort s)
    {
        var reading = new PortReading { Port = s };
        using (SafeFileHandle h = Native.OpenHub(s.HubPath))
        {
            if (h == null) { reading.Problem = "USB controller not available"; return reading; }
            Dictionary<int, int> nodes = Native.ChildrenByPort(s.HubInstanceId);
            string p3 = null, p2 = null;
            UsbDevice fast = s.Usb3Port > 0 ? ReadPort(h, s.HubPath, s.Usb3Port, nodes, 0, out p3) : null;
            UsbDevice slow = s.Usb2Port > 0 ? ReadPort(h, s.HubPath, s.Usb2Port, nodes, 0, out p2) : null;
            if (fast != null && slow != null && fast.IsHub && slow.IsHub)
            {
                // A USB 3 hub shows up on both halves; its devices are split between them.
                fast.Children.AddRange(slow.Children);
                reading.Device = fast;
            }
            else reading.Device = fast ?? slow;
            if (reading.Device == null) reading.Problem = p3 ?? p2;
        }
        return reading;
    }

    static UsbDevice ReadPort(SafeFileHandle h, string hubPath, int n, Dictionary<int, int> nodes, int depth, out string problem)
    {
        problem = null;
        byte[] ci = new byte[512]; BitConverter.GetBytes(n).CopyTo(ci, 0);
        ci = Native.Ioctl(h, IOCTL_USB_GET_NODE_CONNECTION_INFORMATION_EX, ci);
        if (ci == null) return null;
        int status = BitConverter.ToInt32(ci, 31);
        if (status == 0) return null;                                   // nothing on this port
        if (status != 1) { problem = StatusText(status); return null; }

        var d = new UsbDevice { HubPath = hubPath, ConnectionIndex = n };
        d.DeviceDescriptor = new byte[18];
        Array.Copy(ci, 4, d.DeviceDescriptor, 0, 18);
        d.IsHub = ci[24] != 0;
        d.Address = BitConverter.ToUInt16(ci, 25);
        d.OpenPipes = BitConverter.ToInt32(ci, 27);
        d.Speed = Math.Min((int)ci[23], 2);
        byte[] v2 = Native.ConnectionV2(h, n);
        uint flags = v2 == null ? 0 : BitConverter.ToUInt32(v2, 12);
        if ((flags & 4) != 0) d.Speed = 4;
        else if ((flags & 1) != 0) d.Speed = 3;
        d.Usb3Capable = (flags & 2) != 0 || d.Speed >= 3;

        int dev;
        if (nodes.TryGetValue(n, out dev)) Describe(d, dev);
        else { d.Name = d.IsHub ? "USB hub" : "USB device"; d.Kind = d.Name; }

        if (d.IsHub && d.InstanceId != null && depth < MaxHubDepth) ReadHubChildren(d, d.InstanceId, depth + 1);
        return d;
    }

    static void ReadHubChildren(UsbDevice hub, string hubDevId, int depth)
    {
        foreach (string path in Native.InterfacePaths(Native.GUID_DEVINTERFACE_USB_HUB, hubDevId))
        {
            using (SafeFileHandle h = Native.OpenHub(path))
            {
                if (h == null) continue;
                Dictionary<int, int> nodes = Native.ChildrenByPort(hubDevId);
                int count = Native.PortCount(h);
                for (int n = 1; n <= count; n++)
                {
                    string problem;
                    UsbDevice child = ReadPort(h, path, n, nodes, depth, out problem);
                    if (child != null) hub.Children.Add(child);
                    else if (problem != null) hub.Children.Add(new UsbDevice { Name = "Device on hub port " + n, Kind = "USB device", Problem = problem, HubPath = path, ConnectionIndex = n });
                }
            }
            return;
        }
    }

    public static string StatusText(int status)
    {
        switch (status)
        {
            case 2: return "The device failed to start";
            case 3: return "The device reported an error";
            case 4: return "Power surge: the device drew too much power";
            case 5: return "Not enough power for this device";
            case 6: return "Not enough USB bandwidth";
            case 7: return "Too many hubs chained together";
            case 9: return "Connecting...";
            case 10: return "The device is resetting";
            default: return "Connection problem";
        }
    }

    // ---------------- naming and device type ----------------

    // What a device is (name, type, disks, interfaces, driver) does not change
    // while it stays connected, so background scans reuse it and only re-check
    // speed, status and drive letters. Full scans always read everything afresh.
    class Identity
    {
        public int DevNode;
        public string Name, Kind, DriverName, DriverService, DriverProvider, DriverVersion, DriverFile;
        public DateTime? DriverUpdated;
        public List<string> Disks, Interfaces;
    }

    static readonly Dictionary<string, Identity> identities = new Dictionary<string, Identity>(StringComparer.OrdinalIgnoreCase);
    [ThreadStatic] static bool reuseIdentity;

    static bool TryReuse(UsbDevice d, int dev)
    {
        Identity id;
        lock (identities) if (!identities.TryGetValue(d.InstanceId, out id)) return false;
        if (id.DevNode != dev) return false;
        foreach (string disk in id.Disks) if (Native.Locate(disk) == 0) return false;   // a drive was ejected: look again
        d.Name = id.Name; d.Kind = id.Kind;
        d.DiskInstanceIds.AddRange(id.Disks);
        d.InterfaceClasses = id.Interfaces;
        d.DriverName = id.DriverName; d.DriverService = id.DriverService; d.DriverProvider = id.DriverProvider;
        d.DriverVersion = id.DriverVersion; d.DriverFile = id.DriverFile; d.DriverUpdated = id.DriverUpdated;
        foreach (string disk in id.Disks) d.Drives.AddRange(DrivesOnDisk(disk));
        return true;
    }

    static void Remember(UsbDevice d, int dev)
    {
        var id = new Identity
        {
            DevNode = dev, Name = d.Name, Kind = d.Kind, Disks = new List<string>(d.DiskInstanceIds), Interfaces = d.InterfaceClasses,
            DriverName = d.DriverName, DriverService = d.DriverService, DriverProvider = d.DriverProvider,
            DriverVersion = d.DriverVersion, DriverFile = d.DriverFile, DriverUpdated = d.DriverUpdated
        };
        lock (identities)
        {
            Identity old;
            // keep driver details from an earlier full scan if this one skipped them
            if (id.DriverService == null && identities.TryGetValue(d.InstanceId, out old) && old.DevNode == dev)
            {
                id.DriverName = old.DriverName; id.DriverService = old.DriverService; id.DriverProvider = old.DriverProvider;
                id.DriverVersion = old.DriverVersion; id.DriverFile = old.DriverFile; id.DriverUpdated = old.DriverUpdated;
            }
            identities[d.InstanceId] = id;
        }
    }

    static void Describe(UsbDevice d, int dev)
    {
        d.InstanceId = Native.DeviceId(dev);
        if (d.InstanceId != null && reuseIdentity && TryReuse(d, dev)) return;
        DescribeFresh(d, dev);
        if (d.InstanceId != null) Remember(d, dev);
    }

    static void DescribeFresh(UsbDevice d, int dev)
    {
        var found = new DeviceKinds.Found();
        // A hub's children are the devices plugged into it; they are listed
        // separately and must not decide what the hub itself is.
        Collect(dev, d.IsHub ? 4 : 0, found);

        string self = Native.BusReportedDesc(dev);
        if (string.IsNullOrEmpty(self)) self = Native.RegProp(dev, Native.CM_DRP_FRIENDLYNAME);
        if (string.IsNullOrEmpty(self)) self = Native.RegProp(dev, Native.CM_DRP_DEVICEDESC);
        if (string.IsNullOrEmpty(self)) self = "USB device";

        if (found.Disks.Count > 0)
        {
            int disk = found.Disks[0];
            string name = Native.RegProp(disk, Native.CM_DRP_FRIENDLYNAME) ?? Native.RegProp(disk, Native.CM_DRP_DEVICEDESC);
            d.Name = name == null ? self : name.Replace(" SCSI Disk Device", "").Replace(" USB Device", "").Trim();
            foreach (int x in found.Disks)
            {
                string id = Native.DeviceId(x);
                d.DiskInstanceIds.Add(id);
                d.Drives.AddRange(DrivesOnDisk(id));
            }
        }
        else if (found.CdRoms.Count > 0)
        {
            string name = Native.RegProp(found.CdRoms[0], Native.CM_DRP_FRIENDLYNAME);
            d.Name = name == null ? self : name.Replace(" SCSI CdRom Device", "").Replace(" USB Device", "").Trim();
        }
        else d.Name = self;

        d.InterfaceClasses = found.InterfaceClasses;
        d.Kind = DeviceKinds.Classify(self, d.IsHub, d.DeviceDescriptor[4], found);
        if (!lettersOnly) DriverInfo.Fill(d, dev);
    }

    static void Collect(int dev, int depth, DeviceKinds.Found found)
    {
        string cls = Native.RegProp(dev, Native.CM_DRP_CLASS);
        if (cls != null) found.Classes.Add(cls);
        string name = Native.RegProp(dev, Native.CM_DRP_FRIENDLYNAME) ?? Native.RegProp(dev, Native.CM_DRP_DEVICEDESC);
        if (name != null) found.Names.Add(name);
        List<string> compat = Native.RegMultiProp(dev, Native.CM_DRP_COMPATIBLEIDS);
        if (compat.Count > 0 && compat[0].StartsWith(@"USB\Class_", StringComparison.OrdinalIgnoreCase) && !found.InterfaceClasses.Contains(compat[0]))
            found.InterfaceClasses.Add(compat[0]);
        if (string.Equals(cls, "DiskDrive", StringComparison.OrdinalIgnoreCase)) found.Disks.Add(dev);
        if (string.Equals(cls, "CDROM", StringComparison.OrdinalIgnoreCase)) found.CdRoms.Add(dev);
        if (depth >= 4) return;
        int child;
        if (Native.CM_Get_Child(out child, dev, 0) != 0) return;
        do Collect(child, depth + 1, found);
        while (Native.CM_Get_Sibling(out child, child, 0) == 0);
    }

    // ---------------- drive letters ----------------

    public static List<DriveVolume> DrivesOnDisk(string diskDevId)
    {
        var result = new List<DriveVolume>();
        if (diskDevId == null) return result;
        int number = -1;
        foreach (string p in Native.InterfacePaths(Native.GUID_DEVINTERFACE_DISK, diskDevId)) { number = DiskNumber(p); break; }
        if (number < 0) return result;
        foreach (DriveInfo di in DriveInfo.GetDrives())
        {
            if (di.DriveType != DriveType.Fixed && di.DriveType != DriveType.Removable) continue;
            string letter = di.Name.Substring(0, 2);
            if (DiskNumber(@"\\.\" + letter) != number) continue;
            var v = new DriveVolume { Letter = letter };
            if (!lettersOnly)
            {
                try
                {
                    if (di.IsReady) { v.Label = di.VolumeLabel; v.FileSystem = di.DriveFormat; v.Total = di.TotalSize; v.Free = di.AvailableFreeSpace; }
                }
                catch { }
            }
            result.Add(v);
        }
        return result;
    }

    // Asks the volume/partition manager which disk a volume lives on. It opens
    // with no read/write access, so the disk itself is never touched.
    public static int DiskNumber(string path)
    {
        using (SafeFileHandle h = Native.CreateFile(path, 0, 3, IntPtr.Zero, 3, 0, IntPtr.Zero))
        {
            if (h.IsInvalid) return -1;
            byte[] o = Native.Ioctl(h, IOCTL_STORAGE_GET_DEVICE_NUMBER, new byte[12]);
            return o == null ? -1 : BitConverter.ToInt32(o, 4);
        }
    }
}
