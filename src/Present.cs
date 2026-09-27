// Turns scanner results into the words, colours and icons the windows show.
using System;
using System.Collections.Generic;

public class InfoLine
{
    public string Text;
    public Tone Tone;       // Normal = regular muted text
    public string Glyph;    // optional small icon before the text

    public InfoLine(string text, Tone tone, string glyph) { Text = text; Tone = tone; Glyph = glyph; }
}

static class Present
{
    const string Dot = "  ·  ";

    public static Tone ToneOf(PortReading r)
    {
        if (r.Device == null) return r.Problem != null ? Tone.Bad : Tone.Empty;
        return ToneOf(r.Device);
    }

    public static Tone ToneOf(UsbDevice d)
    {
        if (d.Problem != null) return Tone.Bad;
        if (d.Speed >= 3) return Tone.Good;
        if (d.Usb3Capable) return Tone.Warn;
        return Tone.Normal;
    }

    public static string Generation(int speed)
    {
        return speed >= 3 ? "USB 3" : speed == 2 ? "USB 2" : "USB 1.1";
    }

    public static string Rate(int speed)
    {
        switch (speed)
        {
            case 0: return "1.5 Mbps";
            case 1: return "12 Mbps";
            case 2: return "480 Mbps";
            case 3: return "5 Gbps";
            default: return "10 Gbps";
        }
    }

    public static string SpeedName(int speed)
    {
        switch (speed)
        {
            case 0: return "Low Speed";
            case 1: return "Full Speed";
            case 2: return "High Speed";
            case 3: return "SuperSpeed";
            default: return "SuperSpeed+";
        }
    }

    public static string PortKind(PhysicalPort p)
    {
        string s = p.Usb3Port > 0 ? "USB 3 port" : "USB 2 port";
        return p.IsTypeC ? s + ", USB-C" : s;
    }

    public static string Title(PortReading r)
    {
        if (r.Device != null) return r.Device.Name;
        return r.Problem != null ? "Problem on this port" : "Empty";
    }

    // Short name for the corner panel.
    public static string ShortTitle(PortReading r)
    {
        if (r.Device == null) return r.Problem != null ? "Problem" : "Empty";
        UsbDevice d = r.Device;
        if (d.IsHub) return "USB hub (" + d.Children.Count + (d.Children.Count == 1 ? " device)" : " devices)");
        if (d.Drives.Count > 0) return d.Name + "  (" + d.Drives[0].Letter + ")";
        return d.Name;
    }

    public static string Pill(PortReading r)
    {
        if (r.Device == null) return r.Problem != null ? "Error" : null;
        return Generation(r.Device.Speed) + "  ·  " + Rate(r.Device.Speed);
    }

    public static string ShortPill(PortReading r)
    {
        if (r.Device == null) return r.Problem != null ? "Error" : null;
        return Generation(r.Device.Speed);
    }

    public static string Glyph(PortReading r)
    {
        if (r.Device == null) return r.Problem != null ? Glyphs.Warning : Glyphs.Usb;
        return Glyph(r.Device);
    }

    public static string Glyph(UsbDevice d)
    {
        if (d.Problem != null) return Glyphs.Warning;
        switch (d.Kind)
        {
            case "Storage":
            case "Optical drive": return Glyphs.Drive;
            case "Mouse":
            case "Wireless receiver": return Glyphs.Mouse;
            case "Keyboard":
            case "Mouse and keyboard":
            case "Input device": return Glyphs.Keyboard;
            case "Phone or tablet": return Glyphs.Phone;
            case "Camera":
            case "Camera or scanner": return Glyphs.Camera;
            case "Game controller": return Glyphs.Game;
            case "Audio device":
            case "Headset": return Glyphs.Audio;
            case "Microphone": return Glyphs.Microphone;
            case "Speaker": return Glyphs.Speaker;
            case "Wi-Fi adapter": return Glyphs.Wifi;
            case "Network adapter": return Glyphs.Network;
            case "Printer": return Glyphs.Printer;
            case "Bluetooth adapter": return Glyphs.Bluetooth;
            case "Card reader": return Glyphs.Card;
            case "Security key": return Glyphs.Lock;
            case "Fingerprint reader": return Glyphs.Fingerprint;
            case "Drawing tablet": return Glyphs.Pen;
            case "UPS or battery": return Glyphs.Battery;
            case "Display adapter": return Glyphs.Monitor;
            case "Firmware update mode": return Glyphs.Download;
            default: return Glyphs.Usb;
        }
    }

    public static List<InfoLine> Details(PortReading r)
    {
        var lines = new List<InfoLine>();
        if (r.Device == null)
        {
            if (r.Problem != null) lines.Add(new InfoLine(r.Problem, Tone.Bad, null));
            return lines;
        }
        UsbDevice d = r.Device;
        if (d.IsHub)
            lines.Add(new InfoLine(d.Kind + Dot + d.Children.Count + (d.Children.Count == 1 ? " device plugged in" : " devices plugged in"), Tone.Normal, null));
        else
            lines.Add(new InfoLine(d.Kind + Dot + SpeedName(d.Speed) + " (" + Rate(d.Speed) + ")" + SpeedNote(d), Tone.Normal, null));

        foreach (DriveVolume v in d.Drives) lines.Add(new InfoLine(DriveText(v), Tone.Normal, Glyphs.Drive));

        if (ToneOf(d) == Tone.Warn)
            lines.Add(new InfoLine("Supports USB 3 but connected at USB 2. Reconnect it firmly for full speed.", Tone.Warn, null));

        if (d.DriverName != null || d.DriverService != null)
        {
            string name = d.DriverName ?? d.DriverService;
            if (d.DriverName != null && d.DriverService != null) name += " (" + d.DriverService + ")";
            lines.Add(new InfoLine("Driver: " + name, Tone.Normal, Glyphs.Driver));
            var parts = new List<string>();
            if (d.DriverVersion != null) parts.Add("version " + d.DriverVersion);
            if (d.DriverUpdated.HasValue) parts.Add("updated " + d.DriverUpdated.Value.ToString("d MMM yyyy"));
            if (d.DriverProvider != null) parts.Add(d.DriverProvider);
            if (parts.Count > 0) lines.Add(new InfoLine(string.Join(Dot, parts.ToArray()), Tone.Normal, ""));   // "" = indent under the icon
        }
        else if (d.InstanceId != null)
            lines.Add(new InfoLine("Driver: none installed", Tone.Warn, Glyphs.Driver));

        foreach (UsbDevice c in d.Children)
        {
            string text = c.Problem != null
                ? c.Name + Dot + c.Problem
                : c.Name + Dot + Generation(c.Speed) + " (" + Rate(c.Speed) + ")" + DriveSuffix(c);
            lines.Add(new InfoLine(text, ToneOf(c) == Tone.Good ? Tone.Normal : ToneOf(c), Glyph(c)));
        }
        return lines;
    }

    // Devices that never need more than 12 Mbps; say so, so it isn't read as a fault.
    static readonly string[] slowByDesign = {
        "Mouse", "Keyboard", "Mouse and keyboard", "Input device", "Game controller", "Wireless receiver",
        "Drawing tablet", "Security key", "Fingerprint reader", "UPS or battery", "Serial device", "Card reader",
        "Audio device", "Headset", "Microphone", "Speaker", "Bluetooth adapter", "Sensor"
    };

    static string SpeedNote(UsbDevice d)
    {
        if (d.Speed <= 1 && Array.IndexOf(slowByDesign, d.Kind) >= 0) return Dot + "normal for this device";
        return "";
    }

    static string DriveSuffix(UsbDevice d)
    {
        return d.Drives.Count > 0 ? Dot + d.Drives[0].Letter : "";
    }

    public static string DriveText(DriveVolume v)
    {
        string name = string.IsNullOrEmpty(v.Label) ? "Local disk" : v.Label;
        if (v.Total <= 0) return v.Letter + "  " + name;
        return v.Letter + "  " + name + Dot + Size(v.Free) + " free of " + Size(v.Total);
    }

    public static string Size(long bytes)
    {
        double gb = bytes / 1073741824.0;
        if (gb >= 1000) return (gb / 1024).ToString("0.0") + " TB";
        if (gb >= 10) return gb.ToString("0") + " GB";
        return gb.ToString("0.0") + " GB";
    }

    public static string TrayText(List<PortReading> readings)
    {
        int n = 0;
        foreach (PortReading r in readings) if (r.Device != null) n++;
        string s = "USB Ports: " + n + (n == 1 ? " device" : " devices");
        foreach (PortReading r in readings)
            if (r.Device != null && r.Device.Drives.Count > 0 && s.Length < 40)
                s += ", " + r.Device.Drives[0].Letter + " at " + Generation(r.Device.Speed);
        return s.Length > 63 ? s.Substring(0, 63) : s;
    }
}
