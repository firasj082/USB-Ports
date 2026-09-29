// DeviceKinds: what kind of device something is, from classes, interfaces and names.
static class DeviceKindsTests
{
    static DeviceKinds.Found With(string[] classes, string[] interfaces, string[] names)
    {
        var f = new DeviceKinds.Found();
        if (classes != null) f.Classes.AddRange(classes);
        if (interfaces != null) f.InterfaceClasses.AddRange(interfaces);
        if (names != null) f.Names.AddRange(names);
        return f;
    }

    [Unit]
    public static void HubWinsOverItsChildren()
    {
        Check.Equal("USB hub", DeviceKinds.Classify("Generic USB Hub", true, 0x09, With(new[] { "Mouse" }, null, null)));
        Check.Equal("USB hub", DeviceKinds.Classify("Something", false, 0x09, new DeviceKinds.Found()), "device class 09");
    }

    [Unit]
    public static void DiskMeansStorage()
    {
        DeviceKinds.Found f = With(new[] { "DiskDrive" }, null, null);
        f.Disks.Add(1);
        Check.Equal("Storage", DeviceKinds.Classify("USB Mass Storage Device", false, 0, f));
    }

    [Unit]
    public static void MouseAndKeyboardFromHidInterfaces()
    {
        Check.Equal("Mouse", DeviceKinds.Classify("USB Input Device", false, 0, With(null, new[] { @"USB\Class_03&SubClass_01&Prot_02" }, null)));
        Check.Equal("Keyboard", DeviceKinds.Classify("USB Input Device", false, 0, With(null, new[] { @"USB\Class_03&SubClass_01&Prot_01" }, null)));
        Check.Equal("Wireless receiver", DeviceKinds.Classify("USB Receiver", false, 0,
            With(new[] { "Mouse", "Keyboard" }, null, new[] { "Unifying Receiver" })));
        Check.Equal("Mouse and keyboard", DeviceKinds.Classify("USB Composite Device", false, 0, With(new[] { "Mouse", "Keyboard" }, null, null)));
        Check.Equal("Keyboard", DeviceKinds.Classify("Gaming Keyboard", false, 0, With(new[] { "Mouse", "Keyboard" }, null, null)),
            "its own name wins");
    }

    [Unit]
    public static void CameraAudioAndNetwork()
    {
        Check.Equal("Camera", DeviceKinds.Classify("USB Video Device", false, 0xEF, With(null, new[] { @"USB\Class_0E&SubClass_01&Prot_00" }, null)));
        Check.Equal("Headset", DeviceKinds.Classify("USB Audio", false, 0, With(new[] { "MEDIA" }, null, new[] { "Headset Earphone" })));
        Check.Equal("Wi-Fi adapter", DeviceKinds.Classify("802.11ac WLAN Adapter", false, 0, With(new[] { "Net" }, null, null)));
        Check.Equal("Network adapter", DeviceKinds.Classify("USB 10/100 LAN", false, 0, With(new[] { "Net" }, null, null)));
    }

    [Unit]
    public static void UnknownIsUsbDevice()
    {
        Check.Equal("USB device", DeviceKinds.Classify("USB device", false, 0xFF, new DeviceKinds.Found()));
    }

    [Unit]
    public static void ParsesInterfaceClassIds()
    {
        int c, s, p;
        Check.True(DeviceKinds.Parse(@"USB\Class_03&SubClass_01&Prot_02", out c, out s, out p), "parsed");
        Check.Equal(3, c, "class"); Check.Equal(1, s, "subclass"); Check.Equal(2, p, "protocol");
        Check.True(DeviceKinds.Parse(@"USB\Class_08", out c, out s, out p), "class only");
        Check.Equal(8, c, "class"); Check.Equal(-1, s, "no subclass"); Check.Equal(-1, p, "no protocol");
        Check.False(DeviceKinds.Parse(@"USB\VID_0781&PID_5581", out c, out s, out p), "not a class id");
        Check.Equal("Mass storage (UAS, fast)", DeviceKinds.InterfaceName(8, 6, 0x62));
    }
}
