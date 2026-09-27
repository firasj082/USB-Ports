// The Settings page inside the main window, grouped like Windows 11 Settings:
// a heading per section and a card of rows (title, explanation, switch or button).
// It scrolls with the main window's content area. Changes apply straight away.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

class SettingsView : Control
{
    class Row
    {
        public string Title, Text;
        public Control Control;       // a ToggleSwitch or a FlatButton
        public Func<bool> Read;       // current value, for switches
        public int Top, Height;
    }

    class Section
    {
        public string Title;
        public List<Row> Rows = new List<Row>();
        public Rectangle Card;
        public int TitleTop;
    }

    readonly Theme t;
    readonly float k;
    readonly List<Section> sections = new List<Section>();
    readonly Font fSection, fRow, fText;
    bool loading;

    public event Action<bool> DarkModeChanged;

    public SettingsView(Theme t, float k)
    {
        this.t = t; this.k = k;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        fSection = new Font("Segoe UI Semibold", 10.5f);
        fRow = new Font("Segoe UI Semibold", 9.75f);
        fText = new Font("Segoe UI", 9f);

        Section s = Add("Appearance");
        Switch(s, "Dark mode", "Switches between the dark and light look. The Dark / Light button in the bar on the left does the same.",
            delegate { return t.Dark; }, delegate (bool on) { if (DarkModeChanged != null) DarkModeChanged(on); });

        s = Add("Startup and tray");
        Switch(s, "Start with Windows", "Opens quietly in the system tray when you sign in, with no window. Listed in Task Manager under Startup apps, where it can also be turned off.",
            delegate { return Settings.StartWithWindows; }, delegate (bool on) { Settings.StartWithWindows = on; });
        Switch(s, "Keep running in the tray when closed", "Closing the window hides it and USB Ports keeps working in the tray. Turn this off to make the close button exit the app.",
            delegate { return Settings.CloseToTray; }, delegate (bool on) { Settings.CloseToTray = on; });

        s = Add("Shutdown");
        Switch(s, "Safely eject USB drives at shutdown", "When Windows shuts down or restarts, USB Ports safely removes every USB drive first. It needs to be running for this; in the tray is enough.",
            delegate { return Settings.EjectOnShutdown; }, delegate (bool on) { Settings.EjectOnShutdown = on; });
        Switch(s, "Close apps that keep a drive busy", "If a drive can't be ejected because an app is still using it, USB Ports asks the app to close (and closes it after 3 seconds if it doesn't), then ejects the drive. Windows waits for this, at most 30 seconds.",
            delegate { return Settings.CloseAppsAtShutdown; }, delegate (bool on) { Settings.CloseAppsAtShutdown = on; });
        Button(s, "Shutdown log", "What was ejected at each shutdown, and which apps had to be closed.", "", "Open", delegate { OpenFile(Settings.ShutdownLogPath); });

        s = Add("Alerts");
        Switch(s, "Alert me about USB problems", "A notification when a USB drive disconnects unexpectedly, when a device that supports USB 3 connects at USB 2, or when a port reports an error. It only reacts when Windows reports a device change, so it costs nothing while idle.",
            delegate { return Settings.UsbAlerts; }, delegate (bool on) { Settings.UsbAlerts = on; });

        s = Add("Reconnect");
        Switch(s, "USB ports share power", "Recommended. Reconnect only runs while no other USB drive is plugged in, because switching one port off and on can disrupt a drive on another port. Turn off only if you are sure your ports are powered separately.",
            delegate { return Settings.PortsSharePower; }, delegate (bool on) { Settings.PortsSharePower = on; });

        s = Add("About");
        Version v = Assembly.GetExecutingAssembly().GetName().Version;
        Button(s, "USB Ports " + v.Major + "." + v.Minor + "." + v.Build, "Source code, updates and the download link are on GitHub.", "", "GitHub page",
            delegate { try { Process.Start("https://github.com/firasj082/USB-Ports"); } catch { } });
        Button(s, "App log", "When USB Ports started and stopped, and any errors. Useful if it doesn't start with Windows.", "", "Open", delegate { OpenFile(AppLog.PathName); });

        Relayout();
    }

    int S(float v) { return (int)Math.Round(v * k); }

    Section Add(string title)
    {
        var s = new Section { Title = title };
        sections.Add(s);
        return s;
    }

    void Switch(Section s, string title, string text, Func<bool> read, Action<bool> apply)
    {
        var sw = new ToggleSwitch(t, k) { Checked = read() };
        sw.CheckedChanged += delegate
        {
            if (loading) return;
            try { apply(sw.Checked); }
            catch (Exception ex) { MessageBox.Show(FindForm(), "Could not change this setting: " + ex.Message, "USB Ports", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        };
        Controls.Add(sw);
        s.Rows.Add(new Row { Title = title, Text = text, Control = sw, Read = read });
    }

    void Button(Section s, string title, string text, string glyph, string label, EventHandler click)
    {
        var b = new FlatButton(t, k, glyph, label, false) { OnCard = true };
        b.Click += click;
        Controls.Add(b);
        s.Rows.Add(new Row { Title = title, Text = text, Control = b });
    }

    static void OpenFile(string path)
    {
        try
        {
            if (!File.Exists(path)) File.WriteAllText(path, "");
            Process.Start("notepad.exe", "\"" + path + "\"");
        }
        catch { }
    }

    // Show the current values (they can change elsewhere, e.g. in Task Manager).
    public void Reload()
    {
        loading = true;
        try
        {
            foreach (Section s in sections)
                foreach (Row r in s.Rows)
                {
                    var sw = r.Control as ToggleSwitch;
                    if (sw != null && r.Read != null) sw.Checked = r.Read();
                }
        }
        finally { loading = false; }
    }

    protected override void OnResize(EventArgs e) { base.OnResize(e); Relayout(); }

    public void Relayout()
    {
        int w = Math.Max(S(300), Width);
        int y = 0;
        foreach (Section s in sections)
        {
            s.TitleTop = y;
            y += S(30);
            int cardTop = y;
            foreach (Row r in s.Rows)
            {
                // the same width OnPaint uses: card left + 16 .. control left - 16
                int textWidth = (w - S(16) - r.Control.Width - S(16)) - S(16);
                int th = Theme.WrappedHeight(this, r.Text, fText, textWidth);
                r.Top = y;
                r.Height = S(14) + S(22) + th + S(14);
                r.Control.Location = new Point(w - S(16) - r.Control.Width, y + (r.Height - r.Control.Height) / 2);
                y += r.Height;
            }
            s.Card = new Rectangle(0, cardTop, w - 1, y - cardTop);
            y += S(22);
        }
        int h = Math.Max(S(60), y);
        if (Height != h) Height = h;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.Clear(t.Back);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        foreach (Section s in sections)
        {
            TextRenderer.DrawText(g, s.Title, fSection, new Rectangle(S(2), s.TitleTop, Width, S(26)), t.Text, t.Back,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            Theme.FillRound(g, t.Card, s.Card, S(8));
            Theme.StrokeRound(g, t.Border, s.Card, S(8));
            for (int i = 0; i < s.Rows.Count; i++)
            {
                Row r = s.Rows[i];
                if (i > 0) using (var pen = new Pen(t.Border)) g.DrawLine(pen, s.Card.X + S(16), r.Top, s.Card.Right - S(16), r.Top);
                int x = s.Card.X + S(16);
                int textRight = r.Control.Left - S(16);
                TextRenderer.DrawText(g, r.Title, fRow, new Rectangle(x, r.Top + S(12), textRight - x, S(24)), t.Text, t.Card,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
                TextRenderer.DrawText(g, r.Text, fText, new Rectangle(x, r.Top + S(36), textRight - x, r.Height - S(36) - S(12)), t.Muted, t.Card,
                    TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
            }
        }
    }
}
