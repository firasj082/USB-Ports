// DriveEjector: what a refused eject means, and the shutdown handler's record keeping.
// The shutdown tests use a stand-in for "eject everything": no real drive is touched.
using System;
using System.IO;

static class DriveEjectorTests
{
    [Unit]
    public static void RefusalTexts()
    {
        Check.Null(DriveEjector.RefusalText(0, 0, ""), "success");
        Check.Equal("an app is using it (vlc.exe)", DriveEjector.RefusalText(0x17, 3, "vlc.exe"));
        Check.Equal("a Windows service is using it (WSearch)", DriveEjector.RefusalText(0x17, 4, "WSearch"));
        Check.Equal("a file or folder on it is open", DriveEjector.RefusalText(0x17, 5, ""));
        Check.Equal("Windows did not allow this app to eject it", DriveEjector.RefusalText(0x17, 12, "x"));
        Check.Equal("something is using it (code 23)", DriveEjector.RefusalText(23, 0, null));
        Check.Equal("something is using it (Disk)", DriveEjector.RefusalText(0x17, 6, "Disk"));
    }

    static string ShutdownLog() { return File.Exists(Settings.ShutdownLogPath) ? File.ReadAllText(Settings.ShutdownLogPath) : ""; }

    static Func<IntPtr, string> MustNotEject { get { return delegate { throw new CheckFailed("must not eject: it is turned off"); }; } }

    [Unit]
    public static void ShutdownWithEjectOff()
    {
        Settings.EjectOnShutdown = false;
        DriveEjector.HandleShutdown(IntPtr.Zero, MustNotEject);
        Check.Contains("Shutdown or restart: ejecting is turned off in Settings.", ShutdownLog());
        Check.Null(Settings.LastShutdownReport, "report");
    }

    [Unit]
    public static void ShutdownEjectsAndKeepsTheReport()
    {
        int calls = 0;
        DriveEjector.HandleShutdown(IntPtr.Zero, delegate { calls++; return "At the last shutdown, USB Ports safely ejected SanDisk (E:)."; });
        Check.Equal(1, calls, "eject calls");
        string log = ShutdownLog();
        Check.Contains("Shutdown or restart: ejecting USB drives...", log);
        Check.Contains("safely ejected SanDisk (E:).", log);
        Check.Equal("At the last shutdown, USB Ports safely ejected SanDisk (E:).", Settings.LastShutdownReport);
    }

    [Unit]
    public static void ShutdownWithNoDrives()
    {
        DriveEjector.HandleShutdown(IntPtr.Zero, delegate { return null; });
        Check.Contains("No USB drives were attached.", ShutdownLog());
        Check.Null(Settings.LastShutdownReport, "report");
    }
}
