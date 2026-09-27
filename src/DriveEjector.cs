// "Safely remove" for USB drives: used by Reconnect (one drive) and at
// shutdown (every USB drive on every port, including behind hubs).
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

static class DriveEjector
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern bool ShutdownBlockReasonCreate(IntPtr hwnd, string reason);
    [DllImport("user32.dll")]
    static extern bool ShutdownBlockReasonDestroy(IntPtr hwnd);
    [DllImport("kernel32.dll")]
    static extern bool SetProcessShutdownParameters(int level, int flags);

    // Ask Windows to tell this app about shutdown late, after the apps that
    // might still be using the drives have closed.
    public static void AskToBeToldLateAboutShutdown()
    {
        try { SetProcessShutdownParameters(0x100, 0); } catch { }
    }

    // Returns null on success, otherwise why Windows refused.
    public static string TryEject(string usbInstanceId)
    {
        int dev = Native.Locate(usbInstanceId);
        if (dev == 0) return "the drive is no longer connected";
        int veto;
        var vetoName = new StringBuilder(512);
        int cr = Native.CM_Request_Device_Eject(dev, out veto, vetoName, vetoName.Capacity, 0);
        if (cr == 0 && veto == 0) return null;
        string who = vetoName.Length > 0 ? " (" + vetoName + ")" : "";
        switch (veto)
        {
            case 3: return "an app is using it" + who;
            case 4: return "a Windows service is using it" + who;
            case 5: return "a file or folder on it is open" + who;
            case 12: return "Windows did not allow this app to eject it";
            default: return "something is using it" + who + (veto == 0 ? " (code " + cr + ")" : "");
        }
    }

    public static List<UsbDevice> UsbDrives() { return UsbDrives(false); }

    static List<UsbDevice> UsbDrives(bool forShutdown)
    {
        var drives = new List<UsbDevice>();
        foreach (PhysicalPort p in UsbScanner.FindPhysicalPorts())
        {
            PortReading r = forShutdown ? UsbScanner.ReadForShutdown(p) : UsbScanner.Read(p);
            if (r.Device != null) AddDrives(r.Device, drives);
        }
        return drives;
    }

    // A short record of what happened at each shutdown (last 200 lines kept).
    public static void Log(string line)
    {
        try
        {
            string path = Settings.ShutdownLogPath;
            var lines = new List<string>();
            if (File.Exists(path)) lines.AddRange(File.ReadAllLines(path));
            lines.Add(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + line);
            if (lines.Count > 200) lines.RemoveRange(0, lines.Count - 200);
            File.WriteAllLines(path, lines.ToArray());
        }
        catch { }
    }

    static void AddDrives(UsbDevice d, List<UsbDevice> drives)
    {
        if (d.DiskInstanceIds.Count > 0 && d.InstanceId != null) drives.Add(d);
        foreach (UsbDevice c in d.Children) AddDrives(c, drives);
    }

    // Ejects every USB drive. Runs while Windows is shutting down, so it
    // tells Windows why it needs a moment. Returns a one-line report.
    public static string EjectAllForShutdown(IntPtr hwnd)
    {
        try { ShutdownBlockReasonCreate(hwnd, "Safely removing USB drives..."); } catch { }
        var done = new List<string>();
        var failed = new List<string>();
        try
        {
            foreach (UsbDevice d in UsbDrives(true))
            {
                string label = Label(d);
                string problem;
                try { problem = TryEject(d.InstanceId); }
                catch (Exception ex) { problem = ex.Message; }
                if (problem == null) done.Add(label);
                else failed.Add(label + ": " + problem);
            }
        }
        catch (Exception ex) { failed.Add(ex.Message); }
        finally
        {
            try { ShutdownBlockReasonDestroy(hwnd); } catch { }
        }
        if (done.Count == 0 && failed.Count == 0) return null;   // no USB drives: nothing to report
        var sb = new StringBuilder("At the last shutdown");
        if (done.Count > 0) sb.Append(", USB Ports safely ejected ").Append(string.Join(", ", done.ToArray()));
        if (failed.Count > 0) sb.Append(done.Count > 0 ? ". It could not eject " : ", USB Ports could not eject ").Append(string.Join("; ", failed.ToArray()));
        return sb.Append('.').ToString();
    }

    static string Label(UsbDevice d)
    {
        var letters = new List<string>();
        foreach (DriveVolume v in d.Drives) letters.Add(v.Letter);
        return letters.Count > 0 ? d.Name + " (" + string.Join(", ", letters.ToArray()) + ")" : d.Name;
    }
}
