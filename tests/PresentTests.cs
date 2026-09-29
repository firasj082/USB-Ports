// Present: the words, tones and lines the port cards show.
using System.Collections.Generic;

static class PresentTests
{
    static List<string> Texts(PortReading r)
    {
        var list = new List<string>();
        foreach (InfoLine l in Present.Details(r)) list.Add(l.Text);
        return list;
    }

    [Unit]
    public static void EmptyPort()
    {
        var r = new PortReading { Port = Fake.Port(1) };
        Check.Equal("Empty", Present.Title(r));
        Check.Equal(Tone.Empty, Present.ToneOf(r));
        Check.Equal(0, Present.Details(r).Count, "lines");
        Check.Null(Present.Pill(r), "pill");
    }

    [Unit]
    public static void PortWithProblem()
    {
        PortReading r = Fake.Problem(Fake.Port(1), "The device failed to start");
        Check.Equal("Problem on this port", Present.Title(r));
        Check.Equal(Tone.Bad, Present.ToneOf(r));
        Check.Equal("Error", Present.Pill(r));
        List<InfoLine> lines = Present.Details(r);
        Check.Equal(1, lines.Count, "lines");
        Check.Equal(Tone.Bad, lines[0].Tone, "tone");
    }

    [Unit]
    public static void DriveAtUsb3()
    {
        UsbDevice d = Fake.Drive("USB\\VID_0781&PID_5581\\1", "SanDisk", "E:", 3);
        d.Drives[0].Total = 16L * 1073741824; d.Drives[0].Free = 5L * 1073741824;
        PortReading r = Fake.Reading(Fake.Port(1), d);
        Check.Equal(Tone.Good, Present.ToneOf(r));
        Check.Equal("USB 3  ·  5 Gbps", Present.Pill(r));
        List<string> t = Texts(r);
        Check.Equal("Storage  ·  SuperSpeed (5 Gbps)", t[0]);
        Check.Equal("E:  SANDISK  ·  5.0 GB free of 16 GB", t[1]);
        Check.Contains("Driver: USB Attached SCSI (UAS) Compatible Device (UASPStor)", t[2]);
    }

    // Quick scans and ejected drives have no letters: no drive line, nothing broken.
    [Unit]
    public static void DriveWithoutLetters()
    {
        PortReading r = Fake.Reading(Fake.Port(1), Fake.Drive("USB\\VID_0781&PID_5581\\1", "SanDisk", null, 3));
        foreach (InfoLine l in Present.Details(r)) Check.False(l.Glyph == Glyphs.Drive, "drive line: " + l.Text);
        Check.Equal("SanDisk", Present.ShortTitle(r));
    }

    [Unit]
    public static void DriveTextWithoutSize()
    {
        Check.Equal("E:  Local disk", Present.DriveText(new DriveVolume { Letter = "E:" }));
    }

    [Unit]
    public static void Usb3DeviceAtUsb2IsFlagged()
    {
        UsbDevice d = Fake.Drive("USB\\VID_0781&PID_5581\\1", "SanDisk", "E:", 2);
        d.Usb3Capable = true;
        PortReading r = Fake.Reading(Fake.Port(1), d);
        Check.Equal(Tone.Warn, Present.ToneOf(r));
        bool found = false;
        foreach (InfoLine l in Present.Details(r))
            if (l.Tone == Tone.Warn && l.Text.Contains("connected at USB 2")) found = true;
        Check.True(found, "USB 2 warning line");
    }

    [Unit]
    public static void SlowMouseIsNormal()
    {
        PortReading r = Fake.Reading(Fake.Port(1), Fake.Device("USB\\VID_046D&PID_C08B\\1", "Gaming mouse", "Mouse", 1));
        Check.Equal(Tone.Normal, Present.ToneOf(r));
        Check.Equal("Mouse  ·  Full Speed (12 Mbps)  ·  normal for this device", Texts(r)[0]);
    }

    [Unit]
    public static void DeviceWithoutDriver()
    {
        UsbDevice d = Fake.Device("USB\\VID_1234&PID_0001\\1", "Gadget", "USB device", 2);
        d.DriverName = null; d.DriverService = null;
        List<InfoLine> lines = Present.Details(Fake.Reading(Fake.Port(1), d));
        Check.Equal("Driver: none installed", lines[lines.Count - 1].Text);
        Check.Equal(Tone.Warn, lines[lines.Count - 1].Tone, "tone");
    }

    [Unit]
    public static void HubListsItsDevices()
    {
        UsbDevice drive = Fake.Drive("USB\\VID_0781&PID_5581\\1", "SanDisk", "E:", 3);
        UsbDevice failed = new UsbDevice { Name = "Device on hub port 3", Kind = "USB device", Problem = "The device failed to start" };
        PortReading r = Fake.Reading(Fake.Port(2), Fake.Hub("USB\\VID_05E3&PID_0626\\H", 3, drive, failed));
        List<InfoLine> lines = Present.Details(r);
        Check.Equal("USB hub  ·  2 devices plugged in", lines[0].Text);
        Check.Equal("USB hub (2 devices)", Present.ShortTitle(r));
        Check.Equal("SanDisk  ·  USB 3 (5 Gbps)  ·  E:", lines[lines.Count - 2].Text);
        Check.Equal("Device on hub port 3  ·  The device failed to start", lines[lines.Count - 1].Text);
        Check.Equal(Tone.Bad, lines[lines.Count - 1].Tone, "failed child tone");
    }

    [Unit]
    public static void TrayTextNamesDrives()
    {
        var readings = Fake.Readings(
            Fake.Reading(Fake.Port(1), Fake.Device("USB\\VID_046D&PID_C08B\\1", "Gaming mouse", "Mouse", 1)),
            Fake.Reading(Fake.Port(2), Fake.Drive("USB\\VID_0781&PID_5581\\1", "SanDisk", "E:", 3)),
            new PortReading { Port = Fake.Port(3) });
        Check.Equal("USB Ports: 2 devices, E: at USB 3", Present.TrayText(readings));
    }

    [Unit]
    public static void SpeedNames()
    {
        Check.Equal("USB 1.1", Present.Generation(1));
        Check.Equal("USB 2", Present.Generation(2));
        Check.Equal("USB 3", Present.Generation(4));
        Check.Equal("10 Gbps", Present.Rate(4));
        Check.Equal("USB 3 port, USB-C", Present.PortKind(new PhysicalPort { Usb3Port = 4, IsTypeC = true }));
        Check.Equal("USB 2 port", Present.PortKind(Fake.Usb2Port(1)));
    }
}
