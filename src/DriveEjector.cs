// "Safely remove" for USB drives: used by Reconnect (one drive) and at
// shutdown (every USB drive on every port, including behind hubs).
using System;
using System.Collections.Generic;
using System.Diagnostics.Eventing.Reader;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

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

    // Windows logs event 1074 (User32) when a shutdown or restart is requested - by the
    // Start menu, the sign-in / lock screen, Windows Update - but not for a sign-out.
    public static bool ShutdownRequestedRecently()
    {
        try
        {
            var query = new EventLogQuery("System", PathType.LogName,
                "*[System[Provider[@Name='User32'] and (EventID=1074) and TimeCreated[timediff(@SystemTime) <= 120000]]]");
            using (var reader = new EventLogReader(query))
            using (EventRecord e = reader.ReadEvent())
                return e != null;
        }
        catch { return false; }
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
        // While this runs, Windows shows "USB Ports is preventing shutdown" with this
        // reason and a "Shut down anyway" button. It never takes longer than the deadline.
        Block(hwnd, "Safely ejecting USB drives...");
        DateTime deadline = DateTime.Now.AddSeconds(30);
        var done = new List<string>();
        var closedApps = new List<string>();
        var pending = new List<UsbDevice>();
        var lastProblem = new Dictionary<UsbDevice, string>();
        try
        {
            pending = UsbDrives(true);
            while (pending.Count > 0)
            {
                foreach (UsbDevice d in pending.ToArray())
                {
                    string problem;
                    try { problem = TryEject(d.InstanceId); }
                    catch (Exception ex) { problem = ex.Message; }
                    if (problem == null) { done.Add(Label(d)); pending.Remove(d); }
                    else lastProblem[d] = problem;
                }
                if (pending.Count == 0 || DateTime.Now >= deadline) break;

                // Something still has a drive open. Apps normally close themselves at
                // shutdown before USB Ports is told; close whatever is left, then retry.
                if (Settings.CloseAppsAtShutdown)
                {
                    var letters = new List<string>();
                    foreach (UsbDevice d in pending) foreach (DriveVolume v in d.Drives) letters.Add(v.Letter);
                    Dictionary<int, string> users = DriveUsers.Find(letters, TimeSpan.FromSeconds(4));
                    if (users.Count > 0)
                    {
                        Log("In use: " + string.Join(", ", letters.ToArray()) + ". Closing " + string.Join(", ", new List<string>(users.Values).ToArray()) + ".");
                        Block(hwnd, "Closing apps that are using USB drives so they can be safely ejected...");
                        foreach (string name in DriveUsers.Close(users, TimeSpan.FromSeconds(3)))
                            if (!closedApps.Contains(name)) closedApps.Add(name);
                        Block(hwnd, "Safely ejecting USB drives...");
                        continue;   // retry straight away
                    }
                }
                Thread.Sleep(1000);   // apps may still be finishing their own shutdown
            }
        }
        catch (Exception ex) { Log("Error while ejecting: " + ex.Message); }
        finally
        {
            try { ShutdownBlockReasonDestroy(hwnd); } catch { }
        }

        if (done.Count == 0 && pending.Count == 0) return null;   // no USB drives: nothing to report
        var sb = new StringBuilder("At the last shutdown");
        if (done.Count > 0) sb.Append(", USB Ports safely ejected ").Append(string.Join(", ", done.ToArray()));
        if (closedApps.Count > 0) sb.Append(" after closing ").Append(string.Join(", ", closedApps.ToArray()));
        if (pending.Count > 0)
        {
            var failed = new List<string>();
            foreach (UsbDevice d in pending)
            {
                string why;
                failed.Add(Label(d) + (lastProblem.TryGetValue(d, out why) ? ": " + why : ""));
            }
            sb.Append(done.Count > 0 ? ". It could not eject " : ", USB Ports could not eject ").Append(string.Join("; ", failed.ToArray()));
        }
        return sb.Append('.').ToString();
    }

    static void Block(IntPtr hwnd, string reason)
    {
        try { ShutdownBlockReasonCreate(hwnd, reason); } catch { }
    }

    static string Label(UsbDevice d)
    {
        var letters = new List<string>();
        foreach (DriveVolume v in d.Drives) letters.Add(v.Letter);
        return letters.Count > 0 ? d.Name + " (" + string.Join(", ", letters.ToArray()) + ")" : d.Name;
    }
}
