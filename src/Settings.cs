// Small per-user settings, kept in HKEY_CURRENT_USER\Software\UsbPorts.
using System;
using System.IO;
using System.Windows.Forms;
using Microsoft.Win32;

static class Settings
{
    static string KeyPath = @"Software\UsbPorts";
    const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string RunValue = "USB Ports";

    // Tests keep their settings and files apart from the owner's: their own registry
    // key and a temp folder. (Start with Windows still reads the real Run entry.)
    const string TestKeyPath = @"Software\UsbPorts.Test";
    static string dataFolderOverride;

    public static void UseTestStorage(string dataFolder)
    {
        KeyPath = TestKeyPath;
        dataFolderOverride = dataFolder;
    }

    public static void DeleteTestStorage()
    {
        if (KeyPath != TestKeyPath) return;
        try { Registry.CurrentUser.DeleteSubKeyTree(TestKeyPath, false); } catch { }
    }

    public static bool UsingTestStorage { get { return KeyPath == TestKeyPath; } }

    public static bool EjectOnShutdown
    {
        get { return GetInt("EjectOnShutdown", 1) != 0; }
        set { SetValue("EjectOnShutdown", value ? 1 : 0); }
    }

    // At shutdown, close apps that keep a USB drive busy so it can still be ejected.
    public static bool CloseAppsAtShutdown
    {
        get { return GetInt("CloseAppsAtShutdown", 1) != 0; }
        set { SetValue("CloseAppsAtShutdown", value ? 1 : 0); }
    }

    // X button: true = hide to the tray, false = close the app completely.
    public static bool CloseToTray
    {
        get { return GetInt("CloseToTray", 1) != 0; }
        set { SetValue("CloseToTray", value ? 1 : 0); }
    }

    // True (default): Reconnect refuses while another USB drive is attached,
    // because power-cycling one port can disrupt drives on the others.
    public static bool PortsSharePower
    {
        get { return GetInt("PortsSharePower", 1) != 0; }
        set { SetValue("PortsSharePower", value ? 1 : 0); }
    }

    // Notifications for unexpected drive disconnects, USB 3 devices stuck at USB 2, and port errors.
    public static bool UsbAlerts
    {
        get { return GetInt("UsbAlerts", 1) != 0; }
        set { SetValue("UsbAlerts", value ? 1 : 0); }
    }

    // -1 = follow Windows (until a choice is made), 0 = light, 1 = dark.
    public static int DarkModeChoice
    {
        get { return GetInt("DarkMode", -1); }
        set { SetValue("DarkMode", value); }
    }

    public static bool TrayHintShown
    {
        get { return GetInt("TrayHintShown", 0) != 0; }
        set { SetValue("TrayHintShown", value ? 1 : 0); }
    }

    // One-line report of what was ejected at the last shutdown, shown once.
    public static string LastShutdownReport
    {
        get { return GetString("LastShutdownReport"); }
        set { if (value == null) Delete("LastShutdownReport"); else SetValue("LastShutdownReport", value); }
    }

    // Windows' own enable / disable flag for startup entries - the switch Task Manager's
    // Startup apps page flips. Current Windows 11 skips entries that have no flag at all,
    // so USB Ports writes "enabled" when you turn Start with Windows on.
    const string ApprovedKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    static readonly byte[] ApprovedEnabled = { 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };

    // Launches quietly into the tray when you sign in (listed in Task Manager > Startup apps).
    public static bool StartWithWindows
    {
        get
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKeyPath))
                    if (k == null || k.GetValue(RunValue) == null) return false;
                return !DisabledInTaskManager();
            }
            catch { return false; }
        }
        set { SetStartup(value, Application.ExecutablePath); }
    }

    static bool DisabledInTaskManager()
    {
        using (RegistryKey k = Registry.CurrentUser.OpenSubKey(ApprovedKeyPath))
            return IsDisabledFlag(k == null ? null : k.GetValue(RunValue) as byte[]);
    }

    // The StartupApproved value: an odd first byte means turned off in Task Manager.
    public static bool IsDisabledFlag(byte[] flag)
    {
        return flag != null && flag.Length > 0 && (flag[0] & 1) != 0;
    }

    // Start with Windows = the Run entry (so it shows and can be toggled in Task
    // Manager > Startup apps) + a sign-in task (so it really starts; see StartupTask).
    public static void SetStartup(bool on, string exePath)
    {
        if (UsingTestStorage) throw new InvalidOperationException("Tests must not change Start with Windows.");
        using (RegistryKey k = Registry.CurrentUser.CreateSubKey(RunKeyPath))
        {
            if (on) k.SetValue(RunValue, RunCommand(exePath));
            else if (k.GetValue(RunValue) != null) k.DeleteValue(RunValue);
        }
        using (RegistryKey k = Registry.CurrentUser.CreateSubKey(ApprovedKeyPath))
        {
            if (on) k.SetValue(RunValue, ApprovedEnabled, RegistryValueKind.Binary);
            else if (k.GetValue(RunValue) != null) k.DeleteValue(RunValue);
        }
        try
        {
            if (on) StartupTask.Register(exePath);
            else StartupTask.Remove();
        }
        catch (Exception ex) { AppLog.Write("Could not " + (on ? "create" : "remove") + " the sign-in task: " + ex.Message); }
        SetValue("AutoStartChosen", 1);
    }

    static string RunCommand(string exePath) { return "\"" + exePath + "\" --tray --autostart"; }

    // Start with Windows is on by default: turn it on the first time the app runs.
    // Later runs keep the entry and the sign-in task pointing at this exe (and add
    // what older versions left out); an on / off choice made in Task Manager is respected.
    public static void ApplyStartupDefault()
    {
        if (UsingTestStorage) return;
        try
        {
            if (GetInt("AutoStartChosen", 0) == 0) { SetStartup(true, Application.ExecutablePath); return; }
            using (RegistryKey run = Registry.CurrentUser.OpenSubKey(RunKeyPath, true))
            {
                if (run == null || run.GetValue(RunValue) == null) return;   // Start with Windows is off
                run.SetValue(RunValue, RunCommand(Application.ExecutablePath));
            }
            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(ApprovedKeyPath))
                if (k.GetValue(RunValue) == null) k.SetValue(RunValue, ApprovedEnabled, RegistryValueKind.Binary);
            if (!StartupTask.IsRegisteredFor(Application.ExecutablePath)) StartupTask.Register(Application.ExecutablePath);
        }
        catch (Exception ex) { AppLog.Write("Startup setup: " + ex.Message); }
    }

    public static string DataFolder
    {
        get
        {
            string dir = dataFolderOverride ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "USB Ports");
            try { Directory.CreateDirectory(dir); } catch { }
            return dir;
        }
    }

    public static string ShutdownLogPath { get { return Path.Combine(DataFolder, "shutdown.log"); } }

    static int GetInt(string name, int fallback)
    {
        try
        {
            using (RegistryKey k = Registry.CurrentUser.OpenSubKey(KeyPath))
            {
                object v = k == null ? null : k.GetValue(name);
                return v is int ? (int)v : fallback;
            }
        }
        catch { return fallback; }
    }

    static string GetString(string name)
    {
        try
        {
            using (RegistryKey k = Registry.CurrentUser.OpenSubKey(KeyPath))
                return k == null ? null : k.GetValue(name) as string;
        }
        catch { return null; }
    }

    static void SetValue(string name, object value)
    {
        try { using (RegistryKey k = Registry.CurrentUser.CreateSubKey(KeyPath)) k.SetValue(name, value); } catch { }
    }

    static void Delete(string name)
    {
        try
        {
            using (RegistryKey k = Registry.CurrentUser.OpenSubKey(KeyPath, true))
                if (k != null && k.GetValue(name) != null) k.DeleteValue(name);
        }
        catch { }
    }
}
