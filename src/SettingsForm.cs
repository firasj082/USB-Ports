// Settings: one card per option with an on/off switch. Changes apply at once.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;

class SettingsForm : Form, IThemed
{
    class Row { public string Title, Text; public ToggleSwitch Switch; public Rectangle Card; }

    readonly Theme t;
    readonly float k;
    readonly List<Row> rows = new List<Row>();
    readonly Font fTitle, fRow, fText;
    readonly FlatButton log, done;
    readonly ToggleSwitch darkSwitch;

    public SettingsForm(Theme t, Icon icon, Action<bool> setDark)
    {
        this.t = t;
        using (Graphics g = CreateGraphics()) k = g.DpiX / 96f;
        Text = "USB Ports - Settings";
        Icon = icon;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        KeyPreview = true;
        DoubleBuffered = true;
        fTitle = new Font("Segoe UI Semibold", 15f);
        fRow = new Font("Segoe UI Semibold", 10.5f);
        fText = new Font("Segoe UI", 9f);

        darkSwitch = Add("Dark mode",
            "Switches between the dark and light look. The moon / sun button in the main window does the same.",
            t.Dark, setDark);
        Add("Start with Windows",
            "Opens quietly in the system tray when you sign in, with no window. It is listed in Task Manager under Startup apps.",
            Settings.StartWithWindows, delegate (bool on) { Settings.StartWithWindows = on; });
        Add("Keep running in the tray when closed",
            "Closing the window hides it and USB Ports keeps working in the tray. Turn this off to make the close button exit the app.",
            Settings.CloseToTray, delegate (bool on) { Settings.CloseToTray = on; });
        Add("Safely eject USB drives at shutdown",
            "When Windows shuts down or restarts, USB Ports safely removes every USB drive first. It needs to be running for this; in the tray is enough.",
            Settings.EjectOnShutdown, delegate (bool on) { Settings.EjectOnShutdown = on; });
        Add("Close apps that keep a drive busy at shutdown",
            "If a drive can't be ejected because an app is still using it, USB Ports asks that app to close (and closes it after 3 seconds if it doesn't), then ejects the drive. Windows waits for this, at most 30 seconds.",
            Settings.CloseAppsAtShutdown, delegate (bool on) { Settings.CloseAppsAtShutdown = on; });
        Add("Alert me about USB problems",
            "Shows a notification when a USB drive disconnects unexpectedly, when a device that supports USB 3 connects at USB 2, or when a port reports an error. It only reacts when Windows reports a device change, so it costs nothing while idle.",
            Settings.UsbAlerts, delegate (bool on) { Settings.UsbAlerts = on; });
        Add("USB ports share power",
            "Recommended. Reconnect only runs while no other USB drive is plugged in, because switching one port off and on can disrupt a drive on another port. Turn off only if you are sure your ports are powered separately.",
            Settings.PortsSharePower, delegate (bool on) { Settings.PortsSharePower = on; });

        log = new FlatButton(t, k, "", "Shutdown log", false);
        log.Click += delegate { OpenLog(); };
        done = new FlatButton(t, k, "", "Done", true);
        done.Click += delegate { Close(); };
        Controls.Add(log);
        Controls.Add(done);
        ActiveControl = done;   // don't open with a focus box on the first switch
        ApplyTheme();
        LayoutAll();
    }

    int S(float v) { return (int)Math.Round(v * k); }

    ToggleSwitch Add(string title, string text, bool on, Action<bool> apply)
    {
        var sw = new ToggleSwitch(t, k) { Checked = on };
        sw.CheckedChanged += delegate
        {
            try { apply(sw.Checked); }
            catch (Exception ex) { MessageBox.Show(this, "Could not change this setting: " + ex.Message, "USB Ports", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        };
        Controls.Add(sw);
        rows.Add(new Row { Title = title, Text = text, Switch = sw });
        return sw;
    }

    public void ApplyTheme()
    {
        BackColor = t.Back;
        ForeColor = t.Text;
        darkSwitch.Checked = t.Dark;
        if (IsHandleCreated) Dwm.DarkTitleBar(Handle, t.Dark);
        Invalidate(true);
    }

    void LayoutAll()
    {
        int width = S(560);
        int y = S(72);
        foreach (Row r in rows)
        {
            // the same width OnPaint draws the text in: card left + 16 .. switch left - 20
            int textWidth = (width - S(20) - S(16) - r.Switch.Width - S(20)) - (S(20) + S(16));
            int th = Theme.WrappedHeight(this, r.Text, fText, textWidth);
            r.Card = new Rectangle(S(20), y, width - S(40), S(16) + S(24) + th + S(16));
            r.Switch.Location = new Point(r.Card.Right - S(16) - r.Switch.Width, r.Card.Y + S(16));
            y = r.Card.Bottom + S(10);
        }
        done.Location = new Point(width - S(20) - done.Width, y + S(8));
        log.Location = new Point(done.Left - S(8) - log.Width, done.Top);
        ClientSize = new Size(width, done.Bottom + S(18));
    }

    void OpenLog()
    {
        string path = Settings.ShutdownLogPath;
        if (!File.Exists(path)) File.WriteAllText(path, "");
        try { Process.Start("notepad.exe", "\"" + path + "\""); } catch { }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Dwm.DarkTitleBar(Handle, t.Dark);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.Escape) Close();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        TextRenderer.DrawText(g, "Settings", fTitle, new Point(S(18), S(18)), t.Text, t.Back, TextFormatFlags.NoPadding);
        foreach (Row r in rows)
        {
            Theme.FillRound(g, t.Card, r.Card, S(8));
            Theme.StrokeRound(g, t.Border, r.Card, S(8));
            int x = r.Card.X + S(16);
            int w = r.Switch.Left - S(20) - x;
            TextRenderer.DrawText(g, r.Title, fRow, new Rectangle(x, r.Card.Y + S(14), w, S(24)), t.Text, t.Card,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, r.Text, fText, new Rectangle(x, r.Card.Y + S(40), w, r.Card.Bottom - S(12) - (r.Card.Y + S(40))), t.Muted, t.Card,
                TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
        }
    }
}

// An on/off switch in the Windows 11 style; the knob slides when flipped.
class ToggleSwitch : Control
{
    readonly Theme t;
    readonly float k;
    readonly Timer anim = new Timer { Interval = 15 };
    bool on, hover;
    float pos;   // 0 = off, 1 = on (animated)
    DateTime lastTick;

    public event EventHandler CheckedChanged;

    public ToggleSwitch(Theme t, float k)
    {
        this.t = t; this.k = k;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        Size = new Size((int)Math.Round(44 * k), (int)Math.Round(22 * k));
        Cursor = Cursors.Hand;
        TabStop = true;
        anim.Tick += delegate
        {
            DateTime now = DateTime.Now;
            float step = (float)Math.Min(0.05, (now - lastTick).TotalSeconds) / 0.15f;
            lastTick = now;
            float target = on ? 1f : 0f;
            pos = pos < target ? Math.Min(target, pos + step) : Math.Max(target, pos - step);
            Invalidate();
            if (pos == target) anim.Stop();
        };
    }

    public bool Checked
    {
        get { return on; }
        set
        {
            if (on == value) return;
            on = value;
            if (!IsHandleCreated || !Visible) pos = on ? 1f : 0f;
            else if (!anim.Enabled) { lastTick = DateTime.Now; anim.Start(); }
            Invalidate();
            if (CheckedChanged != null) CheckedChanged(this, EventArgs.Empty);
        }
    }

    protected override void OnClick(EventArgs e) { base.OnClick(e); Focus(); Checked = !Checked; }
    protected override void OnKeyDown(KeyEventArgs e) { base.OnKeyDown(e); if (e.KeyCode == Keys.Space) Checked = !Checked; }
    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

    protected override void Dispose(bool disposing)
    {
        if (disposing) anim.Dispose();
        base.Dispose(disposing);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.Clear(t.Card);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var track = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        float r = track.Height / 2;
        Color onColor = hover ? t.AccentHover : t.Accent;
        Color offColor = hover ? t.Hover : t.Card;
        Theme.FillRound(g, Theme.Blend(onColor, offColor, pos), track, r);
        if (pos < 1f) using (var pen = new Pen(Color.FromArgb((int)(255 * (1 - pos)), t.Muted))) using (GraphicsPath p = Theme.Round(track, r)) g.DrawPath(pen, p);
        float d = Height * (0.54f + 0.10f * pos);
        float cx = Height / 2f + (Width - Height) * pos;
        Color knob = Theme.Blend(t.Dark ? Color.Black : Color.White, t.Muted, pos);
        using (var b = new SolidBrush(knob)) g.FillEllipse(b, cx - d / 2, Height / 2f - d / 2, d, d);
        if (Focused) using (var pen = new Pen(t.Text) { DashStyle = DashStyle.Dot }) g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
    }
}
