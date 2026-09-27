// Small per-user settings, kept in HKEY_CURRENT_USER\Software\UsbPorts.
using System;
using System.IO;
using System.Windows.Forms;
using Microsoft.Win32;

static class Settings
{
    const string KeyPath = @"Software\UsbPorts";
    const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string RunValue = "USB Ports";

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

    // Launches quietly into the tray when you sign in (listed in Task Manager > Startup apps).
    public static bool StartWithWindows
    {
        get
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKeyPath))
                    return k != null && k.GetValue(RunValue) != null;
            }
            catch { return false; }
        }
        set { SetStartup(value, Application.ExecutablePath); }
    }

    public static void SetStartup(bool on, string exePath)
    {
        using (RegistryKey k = Registry.CurrentUser.CreateSubKey(RunKeyPath))
        {
            if (on) k.SetValue(RunValue, "\"" + exePath + "\" --tray");
            else if (k.GetValue(RunValue) != null) k.DeleteValue(RunValue);
        }
        SetValue("AutoStartChosen", 1);
    }

    // Start with Windows is on by default: turn it on the first time the app runs,
    // and keep the startup entry pointing at the current exe.
    public static void ApplyStartupDefault()
    {
        try
        {
            if (GetInt("AutoStartChosen", 0) == 0) StartWithWindows = true;
            else if (StartWithWindows) StartWithWindows = true;   // refresh the path if the app moved
        }
        catch { }
    }

    public static string DataFolder
    {
        get
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "USB Ports");
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
