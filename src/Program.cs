// Entry point: the command-line switches (install, uninstall, the elevated
// Reconnect helper, portable), the setup window for a downloaded copy, and the
// single-instance handling. The tray app itself is in TrayApp.cs.
using System;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

static class Program
{
    [DllImport("user32.dll")]
    static extern bool SetProcessDPIAware();

    [STAThread]
    static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--reconnect") return Reconnect.RunHelper(args);   // elevated helper
        try { SetProcessDPIAware(); } catch { }
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        if (HasArg(args, "--uninstall")) { Installer.Uninstall(HasArg(args, "--quiet")); return 0; }
        if (HasArg(args, "--install"))   // scripted install: --install [--no-desktop] [--no-startup] [--no-launch]
        {
            Installer.Install(!HasArg(args, "--no-desktop"), !HasArg(args, "--no-startup"), !HasArg(args, "--no-launch"));
            return 0;
        }
        bool portable = HasArg(args, "--portable");
        if (!Installer.IsInstalledCopy && !portable)
        {
            // Opened from a download: offer Install / Update, or Run without installing.
            var setup = new SetupForm(Theme.ForWindows(), TrayApp.LoadIcon(32));
            Application.Run(setup);
            if (setup.Result == SetupForm.Choice.None) return 0;
            if (setup.Result == SetupForm.Choice.Install)
            {
                try { Installer.Install(setup.DesktopShortcut, setup.StartWithWindows, true); }
                catch (Exception ex) { MessageBox.Show("Installing failed: " + ex.Message, "USB Ports", MessageBoxButtons.OK, MessageBoxIcon.Error); return 1; }
                return 0;
            }
            portable = true;
        }

        bool atSignIn = HasArg(args, "--autostart");
        AppLog.Write("Started " + Assembly.GetExecutingAssembly().GetName().Version + (args.Length > 0 ? " (" + string.Join(" ", args) + ")" : ""));
        if (atSignIn && !Settings.StartWithWindows)
        {
            AppLog.Write("Sign-in start skipped: Start with Windows is off (or disabled in Task Manager).");
            return 0;
        }
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += delegate (object s, ThreadExceptionEventArgs e) { AppLog.Write("Error: " + e.Exception); };
        AppDomain.CurrentDomain.UnhandledException += delegate (object s, UnhandledExceptionEventArgs e) { AppLog.Write("Fatal error: " + e.ExceptionObject); };

        bool first;
        using (var mutex = new Mutex(true, @"Local\UsbPortsViewer.Running", out first))
        using (var show = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\UsbPortsViewer.Show"))
        {
            if (!first)
            {
                // Already running. A sign-in launch (both startup paths may fire) stays quiet;
                // opening the app again brings the existing window forward.
                if (atSignIn || HasArg(args, "--tray")) AppLog.Write("Already running; this launch exits.");
                else show.Set();
                return 0;
            }
            using (var exit = new EventWaitHandle(false, EventResetMode.AutoReset, Installer.ExitSignal))
                Application.Run(new TrayApp(show, exit, HasArg(args, "--corner"), HasArg(args, "--tray"), portable));
        }
        AppLog.Write("Exited.");
        return 0;
    }

    static bool HasArg(string[] args, string name)
    {
        return Array.Exists(args, delegate (string a) { return string.Equals(a, name, StringComparison.OrdinalIgnoreCase); });
    }
}
