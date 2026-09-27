// Works out what kind of device something is, from three sources:
//  - the Windows device classes of the device and its sub-devices (Mouse, DiskDrive, Net ...)
//  - the USB interface classes the device declares (USB\Class_0E = video, 0x03 = HID ...)
//  - the names Windows and the device report ("... Webcam", "Wireless Receiver" ...)
using System;
using System.Collections.Generic;

static class DeviceKinds
{
    public class Found
    {
        public List<string> Classes = new List<string>();
        public List<string> Names = new List<string>();
        public List<string> InterfaceClasses = new List<string>();
        public List<int> Disks = new List<int>();
        public List<int> CdRoms = new List<int>();
    }

    public static string Classify(string self, bool isHub, byte deviceClass, Found f)
    {
        string name = self.ToLowerInvariant();
        string all = (self + " | " + string.Join(" | ", f.Names.ToArray())).ToLowerInvariant();

        if (isHub || deviceClass == 0x09) return "USB hub";
        if (f.Disks.Count > 0) return "Storage";
        if (f.CdRoms.Count > 0) return "Optical drive";
        if (HasClass(f, "WPD") || HasClass(f, "AndroidUsbDeviceClass") || Any(all, "iphone", "ipad", "apple mobile", "android", "adb interface", "mtp"))
            return "Phone or tablet";
        if (Any(all, "yubikey", "security key", "fido", "titan key", "solokey", "nitrokey")) return "Security key";
        if (HasClass(f, "Biometric") || Any(all, "fingerprint")) return "Fingerprint reader";
        if (Any(name, "keyboard")) return "Keyboard";
        if (Any(name, "mouse")) return "Mouse";
        if (Any(all, "wacom", "drawing tablet", "pen tablet", "huion", "xp-pen")) return "Drawing tablet";
        if (HasClass(f, "XnaComposite") || HasClass(f, "XboxComposite") || Any(all, "game controller", "gamepad", "joystick", "xbox", "dualshock", "dualsense"))
            return "Game controller";
        if (HasClass(f, "Camera") || Iface(f, 0x0E) || Any(all, "webcam", "camera")) return "Camera";
        if (HasClass(f, "Image") || Iface(f, 0x06)) return "Camera or scanner";
        if (HasClass(f, "Display") || Any(all, "displaylink", "display adapter")) return "Display adapter";

        bool mouse = HasClass(f, "Mouse") || IfaceHid(f, 2);
        bool keyboard = HasClass(f, "Keyboard") || IfaceHid(f, 1);
        if (mouse && keyboard) return Any(all, "receiver", "dongle", "unifying", "wireless") ? "Wireless receiver" : "Mouse and keyboard";
        if (mouse) return "Mouse";
        if (keyboard) return "Keyboard";

        if (HasClass(f, "MEDIA") || HasClass(f, "AudioEndpoint") || Iface(f, 0x01))
            return Any(all, "headset", "headphone", "earphone") ? "Headset" : Any(all, "microphone") ? "Microphone" : Any(all, "speaker") ? "Speaker" : "Audio device";
        if (HasClass(f, "Net") || HasClass(f, "NetClient"))
            return Any(all, "wi-fi", "wifi", "wireless", "802.11", "wlan") ? "Wi-Fi adapter" : "Network adapter";
        if (HasClass(f, "Bluetooth") || IfaceExact(f, 0xE0, 0x01, 0x01)) return "Bluetooth adapter";
        if (HasClass(f, "Printer") || HasClass(f, "PrintQueue") || Iface(f, 0x07)) return "Printer";
        if (HasClass(f, "SmartCardReader") || Iface(f, 0x0B)) return "Card reader";
        if (HasClass(f, "Battery") || Any(all, "ups battery", "hid ups", "uninterruptible")) return "UPS or battery";
        if (HasClass(f, "Sensor")) return "Sensor";
        if (HasClass(f, "Modem")) return "Modem";
        if (HasClass(f, "Ports") || Iface(f, 0x02) || Any(all, "arduino", "serial", "uart", "ch340", "cp210", "ftdi"))
            return "Serial device";
        if (IfaceExact(f, 0xFE, 0x01, -1)) return "Firmware update mode";
        if (Iface(f, 0x11)) return "USB-C accessory";
        if (HasClass(f, "HIDClass") || Iface(f, 0x03)) return "Input device";
        return "USB device";
    }

    static bool Any(string s, params string[] words)
    {
        foreach (string w in words) if (s.Contains(w)) return true;
        return false;
    }

    static bool HasClass(Found f, string c)
    {
        foreach (string x in f.Classes) if (string.Equals(x, c, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    static bool Iface(Found f, int cls) { return IfaceExact(f, cls, -1, -1); }

    static bool IfaceHid(Found f, int protocol) { return IfaceExact(f, 0x03, 0x01, protocol); }

    static bool IfaceExact(Found f, int cls, int sub, int prot)
    {
        foreach (string id in f.InterfaceClasses)
        {
            int c, s, p;
            if (!Parse(id, out c, out s, out p)) continue;
            if (c == cls && (sub < 0 || s == sub) && (prot < 0 || p == prot)) return true;
        }
        return false;
    }

    // "USB\Class_03&SubClass_01&Prot_02" -> 3, 1, 2 (missing parts come back as -1)
    public static bool Parse(string id, out int cls, out int sub, out int prot)
    {
        cls = sub = prot = -1;
        string u = id.ToUpperInvariant();
        int i = u.IndexOf("CLASS_");
        if (i < 0 || !TryHex(u, i + 6, out cls)) return false;
        int j = u.IndexOf("SUBCLASS_");
        if (j >= 0) TryHex(u, j + 9, out sub);
        int k = u.IndexOf("PROT_");
        if (k >= 0) TryHex(u, k + 5, out prot);
        return true;
    }

    static bool TryHex(string s, int at, out int v)
    {
        v = -1;
        if (at + 2 > s.Length) return false;
        return int.TryParse(s.Substring(at, 2), System.Globalization.NumberStyles.HexNumber, null, out v);
    }

    // Plain-English name of a USB interface class, for the Details window.
    public static string InterfaceName(int cls, int sub, int prot)
    {
        switch (cls)
        {
            case 0x01: return sub == 1 ? "Audio control" : sub == 2 ? "Audio streaming" : sub == 3 ? "MIDI" : "Audio";
            case 0x02: return sub == 2 ? "Communications (serial / modem)" : sub == 6 ? "Communications (Ethernet)" : sub == 0x0D ? "Communications (network)" : "Communications";
            case 0x03:
                if (sub == 1 && prot == 1) return "HID keyboard";
                if (sub == 1 && prot == 2) return "HID mouse";
                return "HID (buttons, controls, sensors)";
            case 0x05: return "Physical";
            case 0x06: return "Still image (camera / scanner / phone photos)";
            case 0x07: return "Printer";
            case 0x08: return prot == 0x62 ? "Mass storage (UAS, fast)" : prot == 0x50 ? "Mass storage (bulk-only)" : "Mass storage";
            case 0x09: return "Hub";
            case 0x0A: return "Communications data";
            case 0x0B: return "Smart card";
            case 0x0D: return "Content security";
            case 0x0E: return sub == 1 ? "Video control" : sub == 2 ? "Video streaming" : "Video";
            case 0x0F: return "Personal healthcare";
            case 0x10: return "Audio / video";
            case 0x11: return "Billboard (USB-C alternate modes)";
            case 0x12: return "USB-C bridge";
            case 0xDC: return "Diagnostic";
            case 0xE0: return sub == 1 && prot == 1 ? "Bluetooth" : "Wireless controller";
            case 0xEF: return "Miscellaneous";
            case 0xFE: return sub == 1 ? "Firmware update (DFU)" : sub == 2 ? "IrDA bridge" : sub == 3 ? "Test and measurement" : "Application specific";
            case 0xFF: return "Vendor specific";
            default: return "Class 0x" + cls.ToString("X2");
        }
    }
}
