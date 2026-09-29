// Settings: the StartupApproved flag, and that tests never reach the owner's settings.
using System;
using Microsoft.Win32;

static class SettingsTests
{
    [Unit]
    public static void StartupApprovedFlag()
    {
        Check.False(Settings.IsDisabledFlag(null), "no value");
        Check.False(Settings.IsDisabledFlag(new byte[0]), "empty");
        Check.False(Settings.IsDisabledFlag(new byte[] { 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }), "02 = enabled");
        Check.False(Settings.IsDisabledFlag(new byte[] { 6, 0 }), "06 = enabled");
        Check.True(Settings.IsDisabledFlag(new byte[] { 3, 0, 0, 0, 0x10, 0x2E }), "03 = disabled in Task Manager");
        Check.True(Settings.IsDisabledFlag(new byte[] { 1 }), "01 = disabled");
    }

    [Unit]
    public static void TestsUseTheirOwnKeyAndFolder()
    {
        Check.True(Settings.UsingTestStorage, "test storage");
        Check.True(Settings.EjectOnShutdown, "defaults apply to an empty key");
        Settings.EjectOnShutdown = false;
        Check.False(Settings.EjectOnShutdown, "read back");
        using (RegistryKey k = Registry.CurrentUser.OpenSubKey(@"Software\UsbPorts.Test"))
        {
            Check.NotNull(k, "the test key");
            Check.Equal((object)0, k.GetValue("EjectOnShutdown"), "value in the test key");
        }
        Check.Equal(TestRunner.TempDir, Settings.DataFolder, "data folder");
    }

    [Unit]
    public static void TestsCannotChangeStartWithWindows()
    {
        Check.Throws<InvalidOperationException>(delegate { Settings.SetStartup(false, @"C:\nowhere\UsbPorts.exe"); });
    }

    [Unit]
    public static void LastShutdownReportRoundTrip()
    {
        Check.Null(Settings.LastShutdownReport, "empty");
        Settings.LastShutdownReport = "At the last shutdown, USB Ports safely ejected SanDisk (E:).";
        Check.Equal("At the last shutdown, USB Ports safely ejected SanDisk (E:).", Settings.LastShutdownReport);
        Settings.LastShutdownReport = null;
        Check.Null(Settings.LastShutdownReport, "deleted");
    }
}
