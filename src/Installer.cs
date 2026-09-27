// The app installs itself: the downloaded USB-Ports-Setup.exe copies itself to
// %LOCALAPPDATA%\Programs\USB Ports (per user, no admin), adds shortcuts, an
// entry in Windows' installed-apps list, and optionally Start with Windows.
// Uninstall (from Settings > Apps) runs the installed copy with --uninstall.
using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

static class Installer
{
    const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\USBPorts";
    public const string ExitSignal = @"Local\UsbPortsViewer.Exit";

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    static extern bool DeleteFile(string path);

    public static string InstallDir
    {
        get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\USB Ports"); }
    }

    public static string InstalledExe { get { return Path.Combine(InstallDir, "UsbPorts.exe"); } }

    public static bool IsInstalledCopy
    {
        get { return string.Equals(Path.GetFullPath(Application.ExecutablePath), Path.GetFullPath(InstalledExe), StringComparison.OrdinalIgnoreCase); }
    }

    public static bool IsInstalled { get { return File.Exists(InstalledExe); } }

    static string StartMenuShortcut
    {
        get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "USB Ports.lnk"); }
    }

    static string DesktopShortcut
    {
        get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "USB Ports.lnk"); }
    }

    // ---------------- install ----------------

    public static void Install(bool desktopShortcut, bool startWithWindows, bool launch)
    {
        StopRunningCopy();
        Directory.CreateDirectory(InstallDir);
        File.Copy(Application.ExecutablePath, InstalledExe, true);
        DeleteFile(InstalledExe + ":Zone.Identifier");   // already approved by running setup; don't warn again at every start

        // Older versions kept their source code next to the exe.
        try { if (Directory.Exists(Path.Combine(InstallDir, "src"))) Directory.Delete(Path.Combine(InstallDir, "src"), true); } catch { }
        foreach (string f in Directory.GetFiles(InstallDir, "*.cs")) try { File.Delete(f); } catch { }

        Shortcut(StartMenuShortcut);
        if (desktopShortcut) Shortcut(DesktopShortcut);
        else if (File.Exists(DesktopShortcut)) File.Delete(DesktopShortcut);

        using (RegistryKey k = Registry.CurrentUser.CreateSubKey(UninstallKey))
        {
            Version v = Assembly.GetExecutingAssembly().GetName().Version;
            k.SetValue("DisplayName", "USB Ports");
            k.SetValue("DisplayVersion", v.Major + "." + v.Minor + "." + v.Build);
            k.SetValue("Publisher", "firasj082");
            k.SetValue("DisplayIcon", InstalledExe + ",0");
            k.SetValue("InstallLocation", InstallDir);
            k.SetValue("UninstallString", "\"" + InstalledExe + "\" --uninstall");
            k.SetValue("URLInfoAbout", "https://github.com/firasj082/USB-Ports");
            k.SetValue("NoModify", 1, RegistryValueKind.DWord);
            k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            k.SetValue("EstimatedSize", (int)Math.Max(1, new FileInfo(InstalledExe).Length / 1024), RegistryValueKind.DWord);
        }
        Settings.SetStartup(startWithWindows, InstalledExe);
        if (launch) Process.Start(InstalledExe);
    }

    // Ask a running copy to exit (so its exe can be replaced), then make sure.
    static void StopRunningCopy()
    {
        try
        {
            EventWaitHandle exit;
            if (EventWaitHandle.TryOpenExisting(ExitSignal, out exit)) using (exit) exit.Set();
        }
        catch { }
        string me = Path.GetFullPath(Application.ExecutablePath);
        foreach (Process p in Process.GetProcessesByName("UsbPorts"))
        {
            try
            {
                if (p.Id == Process.GetCurrentProcess().Id) continue;
                if (string.Equals(p.MainModule.FileName, me, StringComparison.OrdinalIgnoreCase)) continue;
                if (!p.WaitForExit(5000)) { p.Kill(); p.WaitForExit(3000); }
            }
            catch { }
            finally { p.Dispose(); }
        }
    }

    static void Shortcut(string lnkPath)
    {
        Type shellType = Type.GetTypeFromProgID("WScript.Shell");
        object shell = Activator.CreateInstance(shellType);
        try
        {
            object lnk = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { lnkPath });
            Type t = lnk.GetType();
            t.InvokeMember("TargetPath", BindingFlags.SetProperty, null, lnk, new object[] { InstalledExe });
            t.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, lnk, new object[] { InstallDir });
            t.InvokeMember("IconLocation", BindingFlags.SetProperty, null, lnk, new object[] { InstalledExe + ",0" });
            t.InvokeMember("Description", BindingFlags.SetProperty, null, lnk, new object[] { "See what is plugged into each USB port and how fast it runs" });
            t.InvokeMember("Save", BindingFlags.InvokeMethod, null, lnk, null);
            Marshal.FinalReleaseComObject(lnk);
        }
        finally { Marshal.FinalReleaseComObject(shell); }
    }

    // ---------------- uninstall ----------------

    public static void Uninstall(bool quiet)
    {
        if (!quiet && MessageBox.Show("Remove USB Ports from this PC? Your USB devices are not affected.", "Uninstall USB Ports",
                MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
        StopRunningCopy();
        try { Settings.SetStartup(false, InstalledExe); } catch { }
        foreach (string lnk in new[] { StartMenuShortcut, DesktopShortcut }) try { if (File.Exists(lnk)) File.Delete(lnk); } catch { }
        try { Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false); } catch { }
        try { Registry.CurrentUser.DeleteSubKeyTree(@"Software\UsbPorts", false); } catch { }
        try { if (Directory.Exists(Settings.DataFolder)) Directory.Delete(Settings.DataFolder, true); } catch { }
        // This exe is running from the install folder, so delete it a moment after exiting.
        var psi = new ProcessStartInfo("cmd.exe", "/c timeout /t 2 /nobreak >nul & rmdir /s /q \"" + InstallDir + "\"")
        { CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden, UseShellExecute = false };
        try { Process.Start(psi); } catch { }
        if (!quiet) MessageBox.Show("USB Ports has been removed.", "Uninstall USB Ports", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }
}
