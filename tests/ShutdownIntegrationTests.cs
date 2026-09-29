// The whole shutdown path inside the test process: a real TrayApp and its hidden main
// window get WM_QUERYENDSESSION then WM_ENDSESSION, as at a real shutdown. The eject is
// a stand-in and the settings are the test key, so no real drive can be ejected.
using System;
using System.IO;
using System.Threading;

static class ShutdownIntegrationTests
{
    const long Logoff = 0x80000000L;

    static string Read(string path) { return File.Exists(path) ? File.ReadAllText(path) : ""; }

    // Sends the two session-end messages; returns how often the eject stand-in ran.
    // Owner's rule (CLAUDE.md): simulated shutdowns only while no USB drive is plugged in,
    // even with the stand-in, because such a test once ejected the owner's SSD twice.
    static int SimulateSessionEnd(long flags)
    {
        Check.True(Settings.UsingTestStorage, "test storage");
        int drives = DriveEjector.UsbDrives().Count;
        if (drives > 0) throw new SkipTest(drives + " USB drive(s) plugged in; simulated shutdowns need none attached.");
        int ejects = 0;
        Func<IntPtr, string> real = TrayApp.EjectAll;
        TrayApp.EjectAll = delegate { ejects++; return null; };
        try
        {
            var show = new EventWaitHandle(false, EventResetMode.AutoReset);
            var exit = new EventWaitHandle(false, EventResetMode.AutoReset);
            var app = new TrayApp(show, exit, false, true, true);   // in the tray, portable
            IntPtr h = app.Window.Handle;
            IntPtr answer = Win32.SendMessage(h, SessionEnd.WM_QUERYENDSESSION, IntPtr.Zero, (IntPtr)flags);
            Check.Equal((IntPtr)1, answer, "WM_QUERYENDSESSION answer (1 = OK to end)");
            Win32.SendMessage(h, SessionEnd.WM_ENDSESSION, (IntPtr)1, (IntPtr)flags);
        }
        finally { TrayApp.EjectAll = real; }
        return ejects;
    }

    [Integration]
    public static void ShutdownWithEjectOffIsRecorded()
    {
        Settings.EjectOnShutdown = false;
        Check.False(Settings.EjectOnShutdown, "eject is off");
        Check.Equal(0, SimulateSessionEnd(0), "ejects");
        Check.Contains("Session ending (confirmed, flags 0x0): treated as shutdown or restart.", Read(AppLog.PathName));
        Check.Contains("Shutdown or restart: ejecting is turned off in Settings.", Read(Settings.ShutdownLogPath));
    }

    [Integration]
    public static void ShutdownWithEjectOnEjects()
    {
        Check.True(Settings.EjectOnShutdown, "eject is on (default)");
        Check.Equal(1, SimulateSessionEnd(0), "ejects");
        string log = Read(Settings.ShutdownLogPath);
        Check.Contains("Shutdown or restart: ejecting USB drives...", log);
        Check.Contains("No USB drives were attached.", log);   // the stand-in found none
    }

    [Integration]
    public static void SignOutDoesNotEject()
    {
        Check.Equal(0, SimulateSessionEnd(Logoff), "ejects");
        Check.Contains("treated as sign-out.", Read(AppLog.PathName));
        Check.Equal("", Read(Settings.ShutdownLogPath), "shutdown.log");
    }
}
