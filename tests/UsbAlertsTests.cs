// UsbAlerts.Compare: which changes between two scans are worth a notification.
using System;
using System.Collections.Generic;

static class UsbAlertsTests
{
    static Func<string, bool> Unexpected(bool value) { return delegate { return value; }; }
    static Func<string, bool> NotAsked { get { return delegate { throw new CheckFailed("the event log must not be asked"); }; } }

    static List<PortReading> Empty(params int[] ports)
    {
        var list = new List<PortReading>();
        foreach (int n in ports) list.Add(new PortReading { Port = Fake.Port(n) });
        return list;
    }

    [Unit]
    public static void DriveGoneUnexpectedlyAlerts()
    {
        var before = Fake.Readings(Fake.Reading(Fake.Port(1), Fake.Drive("USB\\VID_0781&PID_5581\\1", "SanDisk", "E:", 3)));
        List<string> m = UsbAlerts.Compare(before, Empty(1), 0, Unexpected(true));
        Check.Equal(1, m.Count, "messages");
        Check.Contains("SanDisk (E:) disconnected unexpectedly", m[0]);
    }

    [Unit]
    public static void DriveEjectedSafelyIsQuiet()
    {
        var before = Fake.Readings(Fake.Reading(Fake.Port(1), Fake.Drive("USB\\VID_0781&PID_5581\\1", "SanDisk", "E:", 3)));
        Check.Equal(0, UsbAlerts.Compare(before, Empty(1), 0, Unexpected(false)).Count, "messages");
    }

    [Unit]
    public static void DriveBehindHubCounts()
    {
        var drive = Fake.Drive("USB\\VID_0781&PID_5581\\1", "SanDisk", null, 3);
        var before = Fake.Readings(Fake.Reading(Fake.Port(2), Fake.Hub("USB\\VID_05E3&PID_0626\\H", 3, drive)));
        var after = Fake.Readings(Fake.Reading(Fake.Port(2), Fake.Hub("USB\\VID_05E3&PID_0626\\H", 3)));
        List<string> m = UsbAlerts.Compare(before, after, 0, Unexpected(true));
        Check.Equal(1, m.Count, "messages");
        Check.Contains("SanDisk disconnected unexpectedly", m[0]);   // no letters: just the name
    }

    // Other devices come and go by hand all the time: no alert, no event-log query.
    [Unit]
    public static void MouseUnpluggedIsQuiet()
    {
        var before = Fake.Readings(Fake.Reading(Fake.Port(1), Fake.Device("USB\\VID_046D&PID_C08B\\1", "Gaming mouse", "Mouse", 1)));
        Check.Equal(0, UsbAlerts.Compare(before, Empty(1), 0, NotAsked).Count, "messages");
    }

    [Unit]
    public static void NewUsb2FallbackAlertsOnce()
    {
        UsbDevice slow = Fake.Drive("USB\\VID_0781&PID_5581\\1", "SanDisk", "E:", 2);
        slow.Usb3Capable = true;
        var after = Fake.Readings(Fake.Reading(Fake.Port(1), slow));
        List<string> m = UsbAlerts.Compare(Empty(1), after, 0, NotAsked);
        Check.Equal(1, m.Count, "messages");
        Check.Contains("connected at USB 2 (480 Mbps) although it supports USB 3", m[0]);
        Check.Equal(0, UsbAlerts.Compare(after, after, 0, NotAsked).Count, "already known");
    }

    [Unit]
    public static void NewPortProblemAlertsOnce()
    {
        var after = Fake.Readings(Fake.Problem(Fake.Port(2), "Power surge: the device drew too much power"));
        List<string> m = UsbAlerts.Compare(Empty(2), after, 0, NotAsked);
        Check.Equal(1, m.Count, "messages");
        Check.Contains("Port 2: Power surge", m[0]);
        Check.Equal(0, UsbAlerts.Compare(after, after, 0, NotAsked).Count, "same problem again");
    }

    // The port being reconnected by the user is left alone.
    [Unit]
    public static void IgnoredPortIsQuiet()
    {
        UsbDevice slow = Fake.Drive("USB\\VID_0781&PID_5581\\1", "SanDisk", "E:", 2);
        slow.Usb3Capable = true;
        var withDrive = Fake.Readings(Fake.Reading(Fake.Port(1), Fake.Drive("USB\\VID_0781&PID_5581\\1", "SanDisk", "E:", 3)));
        Check.Equal(0, UsbAlerts.Compare(withDrive, Empty(1), 1, NotAsked).Count, "drive gone");
        Check.Equal(0, UsbAlerts.Compare(Empty(1), Fake.Readings(Fake.Reading(Fake.Port(1), slow)), 1, NotAsked).Count, "USB 2 fallback");
        Check.Equal(0, UsbAlerts.Compare(Empty(1), Fake.Readings(Fake.Problem(Fake.Port(1), "Connection problem")), 1, NotAsked).Count, "problem");
    }

    [Unit]
    public static void NothingToCompareIsQuiet()
    {
        Check.Equal(0, UsbAlerts.Compare(null, Empty(1), 0, NotAsked).Count, "first scan");
        Check.Equal(0, UsbAlerts.Compare(Empty(1), null, 0, NotAsked).Count, "no result");
        Check.Equal(0, UsbAlerts.Compare(new List<PortReading>(), new List<PortReading>(), 0, NotAsked).Count, "no ports");
    }
}
