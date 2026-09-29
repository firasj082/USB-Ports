// Made-up ports, devices and scan results for unit tests.
using System.Collections.Generic;

static class Fake
{
    public const string RootHub = @"USB\ROOT_HUB30\4&TEST&0&0";

    // A USB 3 socket: USB 2 half n, USB 3 half n + 10.
    public static PhysicalPort Port(int n)
    {
        return new PhysicalPort { Number = n, Usb2Port = n, Usb3Port = n + 10, HubPath = @"\\?\USB#ROOT_HUB30#TEST", HubInstanceId = RootHub };
    }

    // A socket with no USB 3 half.
    public static PhysicalPort Usb2Port(int n)
    {
        PhysicalPort p = Port(n);
        p.Usb3Port = 0;
        return p;
    }

    // speed: 0 low, 1 full, 2 high (USB 2), 3 SuperSpeed (5 Gbps), 4 SuperSpeed+ (10 Gbps)
    public static UsbDevice Device(string id, string name, string kind, int speed)
    {
        return new UsbDevice
        {
            InstanceId = id, Name = name, Kind = kind, Speed = speed, Usb3Capable = speed >= 3,
            DeviceDescriptor = new byte[18], DriverName = "USB Input Device", DriverService = "HidUsb"
        };
    }

    public static UsbDevice Drive(string id, string name, string letter, int speed)
    {
        UsbDevice d = Device(id, name, "Storage", speed);
        d.DriverName = "USB Attached SCSI (UAS) Compatible Device";
        d.DriverService = "UASPStor";
        d.DiskInstanceIds.Add(@"SCSI\DISK&VEN_TEST\" + id.GetHashCode().ToString("X"));
        if (letter != null) d.Drives.Add(new DriveVolume { Letter = letter, Label = name.ToUpperInvariant(), FileSystem = "exFAT" });
        return d;
    }

    public static UsbDevice Hub(string id, int speed, params UsbDevice[] children)
    {
        UsbDevice h = Device(id, "USB hub", "USB hub", speed);
        h.IsHub = true;
        h.Children.AddRange(children);
        return h;
    }

    public static PortReading Reading(PhysicalPort p, UsbDevice d) { return new PortReading { Port = p, Device = d }; }

    public static PortReading Problem(PhysicalPort p, string problem) { return new PortReading { Port = p, Problem = problem }; }

    public static List<PortReading> Readings(params PortReading[] r) { return new List<PortReading>(r); }
}
