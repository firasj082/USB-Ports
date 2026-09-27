// Entry point, tray icon and refresh timing.
//  - Full window: scans on open, every minute, and on Refresh / F5.
//  - Corner mode: scans every 2 seconds.
//  - Closing the window hides it to the tray (or exits, per Settings).
//  - In the tray it does no polling: it only wakes when Windows reports a
//    device change (for alerts), when the tray icon is hovered, or at shutdown.
//  - At shutdown / restart it can safely eject every USB drive.
//  - Scans run on a background thread so a hung drive can never freeze the UI.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
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

        bool first;
        using (var mutex = new Mutex(true, @"Local\UsbPortsViewer.Running", out first))
        using (var show = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\UsbPortsViewer.Show"))
        {
            if (!first) { show.Set(); return 0; }   // already running: bring that window back instead
            using (var exit = new EventWaitHandle(false, EventResetMode.AutoReset, Installer.ExitSignal))
                Application.Run(new TrayApp(show, exit, HasArg(args, "--corner"), HasArg(args, "--tray"), portable));
        }
        return 0;
    }

    static bool HasArg(string[] args, string name)
    {
        return Array.Exists(args, delegate (string a) { return string.Equals(a, name, StringComparison.OrdinalIgnoreCase); });
    }
}

class TrayApp : ApplicationContext
{
    readonly Theme theme = Theme.ForWindows();
    readonly MainForm main;
    OverlayForm overlay;   // created the first time corner mode is used
    readonly NotifyIcon tray;
    readonly ContextMenuStrip menu;
    readonly Icon windowIcon;
    readonly System.Windows.Forms.Timer everyMinute, everyTwoSeconds, reconnectWatch, deviceSettle;
    SettingsForm settingsForm;
    DateTime lastScanDone = DateTime.MinValue;
    List<PhysicalPort> ports = new List<PhysicalPort>();
    List<PortReading> lastResults;
    bool scanning, exiting, ejectedAtShutdown;
    DateTime scanStarted;
    int generation;
    string lastShutdownReport;  // shown once, the next time the window opens
    int reconnectPort;          // port being reconnected, 0 = none
    string reconnectName;
    DateTime reconnectDeadline;

    public TrayApp(EventWaitHandle showSignal, EventWaitHandle exitSignal, bool startInCorner, bool startInTray, bool portable)
    {
        DriveEjector.AskToBeToldLateAboutShutdown();
        if (!portable) Settings.ApplyStartupDefault();
        lastShutdownReport = Settings.LastShutdownReport;
        Settings.LastShutdownReport = null;

        menu = BuildMenu();
        windowIcon = LoadIcon(32);
        main = new MainForm(theme, windowIcon);
        IntPtr handle = main.Handle;   // scan results are delivered through this window, even while it is hidden
        main.RefreshClicked += delegate { Scan(); };
        main.DetailsClicked += ShowDetails;
        main.ReconnectClicked += StartReconnect;
        main.CornerClicked += delegate { EnterCorner(); };
        main.SettingsClicked += delegate { ShowSettings(); };
        main.ThemeClicked += delegate { SetDark(!theme.Dark); };
        main.DevicesChanged += delegate { deviceSettle.Stop(); deviceSettle.Start(); };
        main.FormClosing += OnMainClosing;
        main.FormClosed += delegate { ExitApp(); };   // only reached when Windows or Task Manager closes it
        main.ShutdownEnding += OnShutdown;


        tray = new NotifyIcon { Icon = LoadIcon(16), Text = "USB Ports", ContextMenuStrip = menu, Visible = true };
        tray.MouseClick += delegate (object s, MouseEventArgs e) { if (e.Button == MouseButtons.Left) OpenMain(); };
        // Keep the tooltip current without polling: refresh when the icon is hovered (at most every 10 s).
        tray.MouseMove += delegate { if (!main.Visible && !OverlayVisible && (DateTime.Now - lastScanDone).TotalSeconds > 10) Scan(); };

        everyMinute = new System.Windows.Forms.Timer { Interval = 60000 };
        everyMinute.Tick += delegate { Scan(); };
        everyTwoSeconds = new System.Windows.Forms.Timer { Interval = 2000 };
        everyTwoSeconds.Tick += delegate { Scan(); };
        reconnectWatch = new System.Windows.Forms.Timer { Interval = 2000 };
        reconnectWatch.Tick += delegate { Scan(); };
        // Device changes arrive in bursts; scan once things settle.
        deviceSettle = new System.Windows.Forms.Timer { Interval = 1500 };
        deviceSettle.Tick += delegate { deviceSettle.Stop(); Scan(); };

        ThreadPool.RegisterWaitForSingleObject(showSignal, delegate
        {
            try { main.BeginInvoke((MethodInvoker)OpenMain); } catch { }
        }, null, -1, false);
        // The installer asks a running copy to exit before replacing it.
        ThreadPool.RegisterWaitForSingleObject(exitSignal, delegate
        {
            try { main.BeginInvoke((MethodInvoker)ExitApp); } catch { }
        }, null, -1, true);

        if (startInCorner) EnterCorner();
        else if (startInTray)
        {
            if (lastShutdownReport != null) tray.ShowBalloonTip(8000, "USB Ports", lastShutdownReport, ToolTipIcon.Info);
            Scan();   // one quiet scan so alerts have something to compare against
        }
        else OpenMain();
    }

    // ---------------- tray menu ----------------

    ContextMenuStrip BuildMenu()
    {
        var m = new ContextMenuStrip();
        m.Renderer = new ToolStripProfessionalRenderer(new MenuColors(theme));
        m.ShowImageMargin = false;
        m.ShowCheckMargin = false;
        m.Font = new Font("Segoe UI", 9.5f);
        AddItem(m, "Open USB Ports", delegate { OpenMain(); }).Font = new Font("Segoe UI Semibold", 9.5f);
        AddItem(m, "Corner mode", delegate { EnterCorner(); });
        AddItem(m, "Refresh now", delegate { Scan(); });
        AddItem(m, "Settings...", delegate { ShowSettings(); });
        m.Items.Add(new ToolStripSeparator());
        AddItem(m, "Exit", delegate { ExitApp(); });
        return m;
    }

    ToolStripMenuItem AddItem(ContextMenuStrip m, string text, EventHandler click)
    {
        var item = new ToolStripMenuItem(text, null, click);
        item.ForeColor = theme.Text;
        item.Padding = new Padding(6, 3, 6, 3);
        m.Items.Add(item);
        return item;
    }

    public static Icon LoadIcon(int size)
    {
        try
        {
            using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream("app.ico"))
                if (s != null) return new Icon(s, size, size);
        }
        catch { }
        return SystemIcons.Application;
    }

    // ---------------- windows ----------------

    void OpenMain()
    {
        if (exiting) return;
        everyTwoSeconds.Stop();
        if (overlay != null) overlay.Hide();
        if (!main.Visible) main.Show();
        if (main.WindowState == FormWindowState.Minimized) main.WindowState = FormWindowState.Normal;
        main.Activate();
        everyMinute.Stop();
        everyMinute.Start();
        if (lastShutdownReport != null)
        {
            main.SetNotice(lastShutdownReport, lastShutdownReport.Contains("could not") ? Tone.Warn : Tone.Good, false);
            lastShutdownReport = null;
        }
        Scan();
    }

    void EnterCorner()
    {
        if (exiting) return;
        everyMinute.Stop();
        main.Hide();
        if (overlay == null)
        {
            overlay = new OverlayForm(Theme.ForOverlay(), menu);
            overlay.OpenRequested += delegate { OpenMain(); };
            if (lastResults != null) overlay.ShowReadings(lastResults, DateTime.Now);
        }
        overlay.Show();
        everyTwoSeconds.Start();
        TrimMemory();   // the big window's memory is not needed while only the panel shows
        Scan();
    }

    // The X button hides the window; the app keeps running in the tray.
    void OnMainClosing(object sender, FormClosingEventArgs e)
    {
        if (exiting || main.ShuttingDown || e.CloseReason != CloseReason.UserClosing) return;
        if (!Settings.CloseToTray) return;   // Settings: close button exits the app (FormClosed -> ExitApp)
        e.Cancel = true;
        main.Hide();
        everyMinute.Stop();
        TrimMemory();
        if (!Settings.TrayHintShown)
        {
            tray.ShowBalloonTip(6000, "USB Ports is still running",
                "It's in the system tray. Click its icon to open it, or right-click for corner mode and Exit.", ToolTipIcon.Info);
            Settings.TrayHintShown = true;
        }
    }

    // ---------------- shutdown ----------------

    void OnShutdown()
    {
        if (ejectedAtShutdown) return;
        ejectedAtShutdown = true;
        everyMinute.Stop();
        everyTwoSeconds.Stop();
        if (!Settings.EjectOnShutdown) { DriveEjector.Log("Shutdown or restart: ejecting is turned off in Settings."); return; }
        DriveEjector.Log("Shutdown or restart: ejecting USB drives...");
        string report = DriveEjector.EjectAllForShutdown(main.Handle);
        DriveEjector.Log(report ?? "No USB drives were attached.");
        if (report != null) Settings.LastShutdownReport = report;
    }

    // ---------------- scanning ----------------

    void Scan()
    {
        if (exiting) return;
        // Skip if a scan is already running, unless it has been stuck for 15 s.
        if (scanning && (DateTime.Now - scanStarted).TotalSeconds < 15) return;
        scanning = true;
        scanStarted = DateTime.Now;
        int gen = ++generation;
        main.SetBusy(true);
        List<PhysicalPort> snapshot = ports;
        bool quick = !main.Visible;   // hidden window: skip details only the full window shows
        ThreadPool.QueueUserWorkItem(delegate
        {
            List<PhysicalPort> layout = snapshot;
            var results = new List<PortReading>();
            try { if (layout.Count == 0) layout = UsbScanner.FindPhysicalPorts(); } catch { }
            foreach (PhysicalPort p in layout)
            {
                try { results.Add(quick ? UsbScanner.ReadQuick(p) : UsbScanner.Read(p)); }
                catch (Exception ex) { results.Add(new PortReading { Port = p, Problem = ex.Message }); }
            }
            try { main.BeginInvoke((MethodInvoker)delegate { Deliver(gen, layout, results); }); } catch { }
        });
    }

    void Deliver(int gen, List<PhysicalPort> layout, List<PortReading> results)
    {
        if (exiting || gen != generation) return;
        scanning = false;
        ports = layout;
        if (Settings.UsbAlerts && !main.ShuttingDown) ShowAlerts(UsbAlerts.Compare(lastResults, results, reconnectPort));
        lastResults = results;
        lastScanDone = DateTime.Now;
        DateTime now = DateTime.Now;
        main.SetBusy(false);
        main.ShowReadings(results, now);
        if (overlay != null) overlay.ShowReadings(results, now);
        tray.Text = Present.TrayText(results);
        if (reconnectPort != 0) CheckReconnect(results);
        if (!main.Visible && !OverlayVisible) TrimMemory();
    }

    bool OverlayVisible { get { return overlay != null && overlay.Visible; } }

    void ShowAlerts(List<string> messages)
    {
        if (messages.Count == 0) return;
        string text = messages[0] + (messages.Count > 1 ? "  (+" + (messages.Count - 1) + " more: open USB Ports)" : "");
        tray.ShowBalloonTip(10000, "USB Ports", text, ToolTipIcon.Warning);
        if (main.Visible) main.SetNotice(string.Join("  ", messages.ToArray()), Tone.Warn, false);
    }

    void ShowSettings()
    {
        if (exiting) return;
        if (settingsForm != null && !settingsForm.IsDisposed) { settingsForm.Activate(); return; }
        settingsForm = new SettingsForm(theme, windowIcon, SetDark);
        if (main.Visible) { settingsForm.Show(main); }
        else { settingsForm.StartPosition = FormStartPosition.CenterScreen; settingsForm.Show(); }
    }

    // Live dark / light switch: recolour the shared theme, then every open window.
    void SetDark(bool dark)
    {
        if (theme.Dark == dark) return;
        Settings.DarkModeChoice = dark ? 1 : 0;
        theme.Apply(dark);
        foreach (Form f in Application.OpenForms)
        {
            var themed = f as IThemed;
            if (themed != null) themed.ApplyTheme();
        }
    }

    [DllImport("kernel32.dll")]
    static extern bool SetProcessWorkingSetSize(IntPtr process, IntPtr min, IntPtr max);

    // Hand unused memory back to Windows while the app is only sitting in the tray.
    static void TrimMemory()
    {
        try
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            SetProcessWorkingSetSize(System.Diagnostics.Process.GetCurrentProcess().Handle, (IntPtr)(-1), (IntPtr)(-1));
        }
        catch { }
    }

    // ---------------- Details ----------------

    void ShowDetails(PortReading r)
    {
        if (r.Device == null) return;
        var f = new DetailsForm(theme, r, windowIcon);
        f.Show(main);
    }

    // ---------------- Reconnect ----------------

    void StartReconnect(PortReading r)
    {
        UsbDevice d = r.Device;
        if (d == null || reconnectPort != 0) return;

        // Refuse up front if another USB drive is attached (the elevated step checks again).
        string blockers = Settings.PortsSharePower ? OtherDrivesIn(lastResults, d) : null;
        if (blockers != null)
        {
            MessageBox.Show(main, Reconnect.BlockedMessage(blockers), "Reconnect " + d.Name, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        string drives = "";
        foreach (DriveVolume v in d.Drives) drives += (drives.Length > 0 ? ", " : "") + v.Letter;
        string text = "This does the same as unplugging " + d.Name + " and plugging it back in. It will disconnect for a few seconds.";
        if (d.DiskInstanceIds.Count > 0)
            text += "\n\nWindows will first safely eject " + (drives.Length > 0 ? drives : "the drive") +
                    ". Close any files, programs, games or installs using it first. If something is still using it, Windows will refuse and nothing will be disconnected.";
        text += Settings.PortsSharePower
            ? "\n\nYour laptop's USB ports share power, so for safety Reconnect only runs while no other USB drive is plugged in."
            : "\n\n\"USB ports share power\" is off in Settings, so other USB drives stay connected. If your ports do share power, they could be disrupted.";
        text += "\n\nWindows will ask for administrator approval.";
        if (MessageBox.Show(main, text, "Reconnect " + d.Name, MessageBoxButtons.OKCancel, MessageBoxIcon.Information) != DialogResult.OK) return;

        reconnectPort = r.Port.Number;
        reconnectName = d.Name;
        main.SetBusyPort(reconnectPort);
        main.SetNotice("Reconnecting " + d.Name + "...", Tone.Normal, true);
        Reconnect.Start(main, d, delegate (bool ok, string message)
        {
            if (!ok)
            {
                EndReconnect();
                bool refused = message.StartsWith("Cancelled") || message.StartsWith("Windows could not safely eject") || message.StartsWith("Reconnect was stopped");
                main.SetNotice(message, refused ? Tone.Warn : Tone.Bad, false);
                Scan();
                return;
            }
            // Give the device time to drop off and come back, then watch for it.
            reconnectDeadline = DateTime.Now.AddSeconds(20);
            main.SetNotice("Reconnecting " + reconnectName + "... waiting for it to come back.", Tone.Normal, true);
            reconnectWatch.Start();
        });
    }

    // Attached USB drives other than the target (including behind hubs), or null.
    static string OtherDrivesIn(List<PortReading> results, UsbDevice target)
    {
        var names = new List<string>();
        if (results != null)
            foreach (PortReading r in results)
                if (r.Device != null) CollectDrives(r.Device, target, names);
        return names.Count > 0 ? string.Join(", ", names.ToArray()) : null;
    }

    static void CollectDrives(UsbDevice d, UsbDevice target, List<string> names)
    {
        bool isTarget = d.InstanceId != null && target.InstanceId != null && string.Equals(d.InstanceId, target.InstanceId, StringComparison.OrdinalIgnoreCase);
        if (!isTarget && d.DiskInstanceIds.Count > 0)
        {
            string letters = "";
            foreach (DriveVolume v in d.Drives) letters += (letters.Length > 0 ? ", " : "") + v.Letter;
            names.Add(letters.Length > 0 ? d.Name + " (" + letters + ")" : d.Name);
        }
        foreach (UsbDevice c in d.Children) CollectDrives(c, target, names);
    }

    void CheckReconnect(List<PortReading> results)
    {
        if (!reconnectWatch.Enabled) return;   // helper still running
        PortReading r = results.Find(delegate (PortReading x) { return x.Port.Number == reconnectPort; });
        bool back = r != null && r.Device != null;
        // The device needs a moment to vanish; don't count the old connection as "back".
        if (back && DateTime.Now < reconnectDeadline.AddSeconds(-16)) return;
        if (!back && DateTime.Now < reconnectDeadline) return;

        string name = reconnectName;
        EndReconnect();
        if (!back)
        {
            main.SetNotice(name + " has not come back yet. If it does not appear shortly, unplug it and plug it back in.", Tone.Warn, false);
            return;
        }
        Tone tone = Present.ToneOf(r);
        string speed = Present.Generation(r.Device.Speed) + " (" + Present.Rate(r.Device.Speed) + ")";
        if (tone == Tone.Warn)
            main.SetNotice("Reconnected, but " + r.Device.Name + " came back at " + speed + ". The connection is not making USB 3 contact; try reseating the cable.", Tone.Warn, false);
        else
            main.SetNotice("Reconnected. " + r.Device.Name + " is running at " + speed + ".", Tone.Good, false);
    }

    void EndReconnect()
    {
        reconnectWatch.Stop();
        reconnectPort = 0;
        reconnectName = null;
        main.SetBusyPort(0);
    }

    void ExitApp()
    {
        if (exiting) return;
        exiting = true;
        everyMinute.Stop();
        everyTwoSeconds.Stop();
        reconnectWatch.Stop();
        deviceSettle.Stop();
        tray.Visible = false;
        tray.Dispose();
        ExitThread();
    }
}

// Tray menu colours that follow the app theme.
class MenuColors : ProfessionalColorTable
{
    readonly Theme t;
    public MenuColors(Theme t) { this.t = t; UseSystemColors = false; }
    public override Color ToolStripDropDownBackground { get { return t.Card; } }
    public override Color ImageMarginGradientBegin { get { return t.Card; } }
    public override Color ImageMarginGradientMiddle { get { return t.Card; } }
    public override Color ImageMarginGradientEnd { get { return t.Card; } }
    public override Color MenuBorder { get { return t.Border; } }
    public override Color MenuItemBorder { get { return t.Hover; } }
    public override Color MenuItemSelected { get { return t.Hover; } }
    public override Color MenuItemSelectedGradientBegin { get { return t.Hover; } }
    public override Color MenuItemSelectedGradientEnd { get { return t.Hover; } }
    public override Color SeparatorDark { get { return t.Border; } }
    public override Color SeparatorLight { get { return t.Card; } }
    public override Color CheckBackground { get { return t.Card; } }
    public override Color CheckSelectedBackground { get { return t.Hover; } }
    public override Color CheckPressedBackground { get { return t.Hover; } }
}
