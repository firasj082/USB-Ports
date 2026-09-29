// Real scans of this PC's ports (read-only: hub IOCTLs, Plug and Play properties and
// volume metadata, as safety rule 1 allows).
using System.Collections.Generic;
using System.Text;

static class ScannerIntegrationTests
{
    // One line per port: what is on it, at what speed, and what is behind a hub.
    static string Describe(List<PortReading> readings)
    {
        var sb = new StringBuilder();
        foreach (PortReading r in readings)
        {
            sb.Append("Port ").Append(r.Port.Number).Append(": ");
            if (r.Device == null) sb.Append(r.Problem ?? "empty");
            else Describe(r.Device, sb);
            sb.Append('\n');
        }
        return sb.ToString();
    }

    static void Describe(UsbDevice d, StringBuilder sb)
    {
        sb.Append(d.InstanceId).Append(" '").Append(d.Name).Append("' ").Append(d.Kind).Append(" speed ").Append(d.Speed);
        foreach (DriveVolume v in d.Drives) sb.Append(' ').Append(v.Letter);
        foreach (UsbDevice c in d.Children) { sb.Append(" ["); Describe(c, sb); sb.Append(']'); }
    }

    static List<PortReading> Scan(List<PhysicalPort> ports, bool quick)
    {
        var list = new List<PortReading>();
        foreach (PhysicalPort p in ports) list.Add(quick ? UsbScanner.ReadQuick(p) : UsbScanner.Read(p));
        return list;
    }

    static string Layout(List<PhysicalPort> ports)
    {
        var sb = new StringBuilder();
        foreach (PhysicalPort p in ports)
            sb.Append(p.Number).Append(": ").Append(p.HubInstanceId).Append(" USB2 ").Append(p.Usb2Port).Append(" USB3 ").Append(p.Usb3Port).Append('\n');
        return sb.ToString();
    }

    [Integration]
    public static void PortLayoutIsStable()
    {
        List<PhysicalPort> a = UsbScanner.FindPhysicalPorts(), b = UsbScanner.FindPhysicalPorts();
        Check.True(a.Count > 0, "found the user-reachable ports");
        Check.Equal(Layout(a), Layout(b), "layout");
    }

    [Integration]
    public static void TwoScansAgree()
    {
        List<PhysicalPort> ports = UsbScanner.FindPhysicalPorts();
        string first = Describe(Scan(ports, false)), second = Describe(Scan(ports, false));
        Check.Equal(first, second, "two full scans");
    }

    // Background scans reuse what devices are; they must still agree with a full scan.
    [Integration]
    public static void QuickScanAgreesWithFullScan()
    {
        List<PhysicalPort> ports = UsbScanner.FindPhysicalPorts();
        string full = Describe(Scan(ports, false)), quick = Describe(Scan(ports, true));
        Check.Equal(full, quick, "full vs quick scan");
    }
}
