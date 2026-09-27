// Windows API plumbing shared by the scanner, Details and Reconnect.
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

static class Native
{
    public static readonly Guid GUID_DEVINTERFACE_USB_HUB = new Guid("f18a0e88-c30c-11d0-8815-00a0c906bed8");
    public static readonly Guid GUID_DEVINTERFACE_DISK = new Guid("53f56307-b6bf-11d0-94f2-00a0c91efb8b");

    public const uint IOCTL_USB_GET_HUB_INFORMATION_EX = 0x220454;
    public const uint IOCTL_USB_GET_NODE_CONNECTION_INFORMATION_EX_V2 = 0x22045C;

    public const int CM_DRP_DEVICEDESC = 0x01, CM_DRP_HARDWAREID = 0x02, CM_DRP_COMPATIBLEIDS = 0x03, CM_DRP_SERVICE = 0x05,
        CM_DRP_CLASS = 0x08, CM_DRP_DRIVER = 0x0A, CM_DRP_MFG = 0x0C, CM_DRP_FRIENDLYNAME = 0x0D, CM_DRP_LOCATION_INFORMATION = 0x0E;

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr sa, uint disp, uint flags, IntPtr tmpl);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool DeviceIoControl(SafeFileHandle h, uint code, byte[] inBuf, int inSize, byte[] outBuf, int outSize, out int returned, IntPtr ov);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    static extern int CM_Get_Device_Interface_List_Size(out int len, ref Guid cls, string devId, int flags);
    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    static extern int CM_Get_Device_Interface_List(ref Guid cls, string devId, char[] buf, int len, int flags);
    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    public static extern int CM_Locate_DevNode(out int devInst, string devId, int flags);
    [DllImport("cfgmgr32.dll")]
    public static extern int CM_Get_Child(out int child, int devInst, int flags);
    [DllImport("cfgmgr32.dll")]
    public static extern int CM_Get_Sibling(out int sibling, int devInst, int flags);
    [DllImport("cfgmgr32.dll")]
    public static extern int CM_Get_Parent(out int parent, int devInst, int flags);
    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    static extern int CM_Get_Device_ID(int devInst, StringBuilder buf, int len, int flags);
    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    static extern int CM_Get_DevNode_Registry_Property(int devInst, int prop, out int type, byte[] buf, ref int len, int flags);
    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    static extern int CM_Get_DevNode_Property(int devInst, ref DEVPROPKEY key, out int type, byte[] buf, ref int len, int flags);
    [DllImport("cfgmgr32.dll")]
    public static extern int CM_Get_DevNode_Status(out int status, out int problem, int devInst, int flags);
    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    public static extern int CM_Request_Device_Eject(int devInst, out int vetoType, StringBuilder vetoName, int nameLength, int flags);

    [StructLayout(LayoutKind.Sequential)]
    public struct DEVPROPKEY
    {
        public Guid fmtid; public int pid;
        public DEVPROPKEY(string guid, int pid) { fmtid = new Guid(guid); this.pid = pid; }
    }

    public static readonly DEVPROPKEY BusReportedDeviceDesc = new DEVPROPKEY("540b947e-8b40-45bc-a8a2-6a0b894cbda2", 4);
    public static readonly DEVPROPKEY DriverDate = new DEVPROPKEY("a8b865dd-2e3d-4094-ad97-e593a70c75d6", 2);
    public static readonly DEVPROPKEY DriverVersion = new DEVPROPKEY("a8b865dd-2e3d-4094-ad97-e593a70c75d6", 3);
    public static readonly DEVPROPKEY DriverDesc = new DEVPROPKEY("a8b865dd-2e3d-4094-ad97-e593a70c75d6", 4);
    public static readonly DEVPROPKEY DriverProvider = new DEVPROPKEY("a8b865dd-2e3d-4094-ad97-e593a70c75d6", 9);
    public static readonly DEVPROPKEY FirstInstallDate = new DEVPROPKEY("83da6326-97a6-4088-9453-a1923f573b29", 101);
    public static readonly DEVPROPKEY LastArrivalDate = new DEVPROPKEY("83da6326-97a6-4088-9453-a1923f573b29", 102);
    public static readonly DEVPROPKEY LocationPaths = new DEVPROPKEY("a45c254e-df1c-4efd-8020-67d146a850e0", 37);

    // ---------------- hubs ----------------

    public static SafeFileHandle OpenHub(string path)
    {
        SafeFileHandle h = CreateFile(path, 0x40000000 /*GENERIC_WRITE*/, 2 /*FILE_SHARE_WRITE*/, IntPtr.Zero, 3, 0, IntPtr.Zero);
        if (h.IsInvalid) { h.Dispose(); return null; }
        return h;
    }

    public static byte[] Ioctl(SafeFileHandle h, uint code, byte[] buf)
    {
        int ignored;
        return Ioctl(h, code, buf, out ignored);
    }

    public static byte[] Ioctl(SafeFileHandle h, uint code, byte[] buf, out int error)
    {
        int ret;
        byte[] outBuf = new byte[buf.Length];
        bool ok = DeviceIoControl(h, code, buf, buf.Length, outBuf, outBuf.Length, out ret, IntPtr.Zero);
        error = ok ? 0 : Marshal.GetLastWin32Error();
        return ok ? outBuf : null;
    }

    public static int PortCount(SafeFileHandle h)
    {
        byte[] hub = Ioctl(h, IOCTL_USB_GET_HUB_INFORMATION_EX, new byte[256]);
        return hub == null ? 0 : BitConverter.ToUInt16(hub, 4);
    }

    // 1 root hub, 2 USB 2 hub, 3 USB 3 hub
    public static int HubType(SafeFileHandle h)
    {
        byte[] hub = Ioctl(h, IOCTL_USB_GET_HUB_INFORMATION_EX, new byte[256]);
        return hub == null ? 0 : BitConverter.ToInt32(hub, 0);
    }

    public static byte[] ConnectionV2(SafeFileHandle h, int n)
    {
        byte[] b = new byte[16];
        BitConverter.GetBytes(n).CopyTo(b, 0);
        BitConverter.GetBytes(16).CopyTo(b, 4);
        BitConverter.GetBytes(7).CopyTo(b, 8);   // we understand USB 1.1, 2.0 and 3.x
        return Ioctl(h, IOCTL_USB_GET_NODE_CONNECTION_INFORMATION_EX_V2, b);
    }

    // \\?\USB#ROOT_HUB30#4&29fe64cb&0&0#{guid}  ->  USB\ROOT_HUB30\4&29fe64cb&0&0
    public static string PathToInstanceId(string path)
    {
        string s = path.Substring(4);
        return s.Substring(0, s.LastIndexOf('#')).Replace('#', '\\');
    }

    // ---------------- device tree ----------------

    public static List<string> InterfacePaths(Guid cls, string devId)
    {
        var list = new List<string>();
        int len;
        if (CM_Get_Device_Interface_List_Size(out len, ref cls, devId, 0) != 0 || len <= 1) return list;
        char[] buf = new char[len];
        if (CM_Get_Device_Interface_List(ref cls, devId, buf, len, 0) != 0) return list;
        foreach (string s in new string(buf).Split('\0')) if (s.Length > 0) list.Add(s);
        return list;
    }

    public static Dictionary<int, int> ChildrenByPort(string hubId)
    {
        var map = new Dictionary<int, int>();
        int hub, child;
        if (CM_Locate_DevNode(out hub, hubId, 0) != 0) return map;
        if (CM_Get_Child(out child, hub, 0) != 0) return map;
        do
        {
            string loc = RegProp(child, CM_DRP_LOCATION_INFORMATION);   // "Port_#0005.Hub_#0001"
            int i = loc == null ? -1 : loc.IndexOf("Port_#");
            int port;
            if (i >= 0 && loc.Length >= i + 10 && int.TryParse(loc.Substring(i + 6, 4), out port)) map[port] = child;
        } while (CM_Get_Sibling(out child, child, 0) == 0);
        return map;
    }

    public static int Locate(string devId)
    {
        int dev;
        return devId != null && CM_Locate_DevNode(out dev, devId, 0) == 0 ? dev : 0;
    }

    public static string DeviceId(int dev)
    {
        var sb = new StringBuilder(512);
        return CM_Get_Device_ID(dev, sb, sb.Capacity, 0) == 0 ? sb.ToString() : null;
    }

    public static List<int> Children(int dev)
    {
        var list = new List<int>();
        int child;
        if (CM_Get_Child(out child, dev, 0) != 0) return list;
        do list.Add(child); while (CM_Get_Sibling(out child, child, 0) == 0);
        return list;
    }

    // ---------------- properties ----------------

    static byte[] RawReg(int dev, int prop, out int len)
    {
        int type; len = 2048; byte[] buf = new byte[len];
        return CM_Get_DevNode_Registry_Property(dev, prop, out type, buf, ref len, 0) == 0 ? buf : null;
    }

    public static string RegProp(int dev, int prop)
    {
        int len; byte[] buf = RawReg(dev, prop, out len);
        return buf == null ? null : Encoding.Unicode.GetString(buf, 0, Math.Max(0, len - 2)).TrimEnd('\0');
    }

    public static List<string> RegMultiProp(int dev, int prop)
    {
        var list = new List<string>();
        int len; byte[] buf = RawReg(dev, prop, out len);
        if (buf == null) return list;
        foreach (string s in Encoding.Unicode.GetString(buf, 0, len).Split('\0')) if (s.Length > 0) list.Add(s);
        return list;
    }

    static byte[] RawProp(int dev, DEVPROPKEY key, out int len)
    {
        int type; len = 2048; byte[] buf = new byte[len];
        return CM_Get_DevNode_Property(dev, ref key, out type, buf, ref len, 0) == 0 ? buf : null;
    }

    public static string StringProp(int dev, DEVPROPKEY key)
    {
        int len; byte[] buf = RawProp(dev, key, out len);
        return buf == null ? null : Encoding.Unicode.GetString(buf, 0, Math.Max(0, len - 2)).TrimEnd('\0');
    }

    public static List<string> StringListProp(int dev, DEVPROPKEY key)
    {
        var list = new List<string>();
        int len; byte[] buf = RawProp(dev, key, out len);
        if (buf == null) return list;
        foreach (string s in Encoding.Unicode.GetString(buf, 0, len).Split('\0')) if (s.Length > 0) list.Add(s);
        return list;
    }

    public static DateTime? DateProp(int dev, DEVPROPKEY key)
    {
        int len; byte[] buf = RawProp(dev, key, out len);
        if (buf == null || len < 8) return null;
        long ft = BitConverter.ToInt64(buf, 0);
        if (ft <= 0) return null;
        try { return DateTime.FromFileTime(ft); } catch { return null; }
    }

    public static string BusReportedDesc(int dev) { return StringProp(dev, BusReportedDeviceDesc); }
}
