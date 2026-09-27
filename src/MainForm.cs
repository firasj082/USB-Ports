// The full window: a header (app badge, title, icon buttons), an optional
// status banner (Reconnect progress, alerts, shutdown report), then one card per port.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

class MainForm : Form, IThemed
{
    readonly Theme t;
    readonly float k;
    readonly IconButton refresh, corner, themeButton, settings;
    readonly ToolTip tips = new ToolTip { InitialDelay = 400 };
    readonly PortListView list;
    readonly Font fTitle, fSub, fFoot, fNotice, fNoticeIcon, fBadge;
    readonly Timer noticeTimer;
    string subtitle = "Scanning...";
    string notice;
    Tone noticeTone;

    public event EventHandler RefreshClicked;
    public event EventHandler CornerClicked;
    public event EventHandler SettingsClicked;
    public event EventHandler ThemeClicked;
    public event Action DevicesChanged;   // Windows says devices were added or removed
    public event Action<PortReading> DetailsClicked;
    public event Action<PortReading> ReconnectClicked;

    public MainForm(Theme t, Icon icon)
    {
        this.t = t;
        using (Graphics g = CreateGraphics()) k = g.DpiX / 96f;
        Text = "USB Ports";
        Icon = icon;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        KeyPreview = true;
        DoubleBuffered = true;

        fTitle = new Font("Segoe UI Semibold", 15f);
        fSub = new Font("Segoe UI", 9f);
        fFoot = new Font("Segoe UI", 8.25f);
        fNotice = new Font("Segoe UI", 9f);
        fNoticeIcon = new Font(t.IconFont, 10f);
        fBadge = new Font(t.IconFont, 14f);

        refresh = AddButton(Glyphs.Refresh, "Refresh now (F5)", true, delegate { Raise(RefreshClicked); });
        corner = AddButton(Glyphs.Pin, "Corner mode: a small panel that stays on top", false, delegate { Raise(CornerClicked); });
        themeButton = AddButton(Glyphs.Moon, "Dark mode", false, delegate { Raise(ThemeClicked); });
        settings = AddButton(Glyphs.Settings, "Settings", false, delegate { Raise(SettingsClicked); });
        settings.TurnOnHover = true;

        list = new PortListView(t, k);
        list.DetailsClicked += delegate (PortReading r) { if (DetailsClicked != null) DetailsClicked(r); };
        list.ReconnectClicked += delegate (PortReading r) { if (ReconnectClicked != null) ReconnectClicked(r); };
        Controls.Add(list);

        noticeTimer = new Timer { Interval = 30000 };
        noticeTimer.Tick += delegate { noticeTimer.Stop(); SetNotice(null, Tone.Normal, false); };

        ApplyTheme();
        LayoutAll();
    }

    int S(float v) { return (int)Math.Round(v * k); }

    IconButton AddButton(string glyph, string tip, bool primary, EventHandler click)
    {
        var b = new IconButton(t, k, glyph, primary);
        b.Click += click;
        tips.SetToolTip(b, tip);
        Controls.Add(b);
        return b;
    }

    void Raise(EventHandler h) { if (h != null) h(this, EventArgs.Empty); }

    public void ApplyTheme()
    {
        BackColor = t.Back;
        ForeColor = t.Text;
        list.BackColor = t.Back;
        themeButton.Glyph = t.Dark ? Glyphs.Sun : Glyphs.Moon;
        tips.SetToolTip(themeButton, t.Dark ? "Light mode" : "Dark mode");
        if (IsHandleCreated) Dwm.DarkTitleBar(Handle, t.Dark);
        Invalidate(true);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Dwm.DarkTitleBar(Handle, t.Dark);
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        CenterToScreen();
    }

    int NoticeBoxHeight
    {
        get
        {
            if (notice == null) return 0;
            int textWidth = S(600) - S(40) - S(52);
            int h = Theme.WrappedHeight(this, notice, fNotice, textWidth);
            return Math.Max(S(40), h + S(18));
        }
    }

    int NoticeHeight { get { return notice == null ? 0 : NoticeBoxHeight + S(10); } }

    void LayoutAll()
    {
        int width = S(600);
        int x = width - S(20);
        foreach (IconButton b in new[] { settings, themeButton, corner, refresh })
        {
            x -= b.Width;
            b.Location = new Point(x, S(24));
            x -= S(8);
        }
        list.Location = new Point(S(20), S(88) + NoticeHeight);
        list.Width = width - S(40);
        ClientSize = new Size(width, list.Bottom + S(42));
        Invalidate();
    }

    public void ShowReadings(List<PortReading> readings, DateTime when)
    {
        list.SetData(readings);
        subtitle = "Updated " + when.ToShortTimeString() + "   ·   refreshes every minute";
        LayoutAll();
    }

    public void SetBusy(bool busy) { refresh.Spinning = busy; }

    public void SetBusyPort(int port) { list.SetBusyPort(port); }

    // A coloured status line under the header. Sticky notices stay until replaced.
    public void SetNotice(string text, Tone tone, bool sticky)
    {
        notice = text;
        noticeTone = tone;
        noticeTimer.Stop();
        if (text != null && !sticky) noticeTimer.Start();
        LayoutAll();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.F5) Raise(RefreshClicked);
    }

    // ---------------- Windows shutting down ----------------

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern int GetSystemMetrics(int index);

    public bool ShuttingDown;
    public event Action ShutdownEnding;   // raised once the shutdown or restart is definite

    protected override void WndProc(ref Message m)
    {
        const int WM_QUERYENDSESSION = 0x11, WM_ENDSESSION = 0x16, WM_DEVICECHANGE = 0x219, DBT_DEVNODES_CHANGED = 7, SM_SHUTTINGDOWN = 0x2000;
        if (m.Msg == WM_DEVICECHANGE && m.WParam.ToInt64() == DBT_DEVNODES_CHANGED && DevicesChanged != null) DevicesChanged();
        if (m.Msg == WM_QUERYENDSESSION || m.Msg == WM_ENDSESSION)
        {
            // A plain sign-out leaves the drives attached to this PC; only act on shutdown / restart.
            bool signOutOnly = (m.LParam.ToInt64() & 0x80000000L) != 0 && GetSystemMetrics(SM_SHUTTINGDOWN) == 0;
            if (!signOutOnly) ShuttingDown = true;
            // Act before WinForms starts closing windows, so the app is still alive to do it.
            if (m.Msg == WM_ENDSESSION && m.WParam != IntPtr.Zero && !signOutOnly && ShutdownEnding != null) ShutdownEnding();
        }
        base.WndProc(ref m);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        // app badge: the same blue USB mark as the app icon
        var badge = new Rectangle(S(20), S(24), S(40), S(40));
        using (var grad = new LinearGradientBrush(badge, Color.FromArgb(37, 99, 235), Color.FromArgb(6, 182, 212), 45f))
        using (GraphicsPath p = Theme.Round(badge, S(10))) g.FillPath(grad, p);
        TextRenderer.DrawText(g, Glyphs.Usb, fBadge, badge, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

        TextRenderer.DrawText(g, "USB Ports", fTitle, new Point(S(72), S(20)), t.Text, t.Back, TextFormatFlags.NoPadding);
        TextRenderer.DrawText(g, subtitle, fSub, new Point(S(73), S(50)), t.Muted, t.Back, TextFormatFlags.NoPadding);

        if (notice != null)
        {
            Color c = noticeTone == Tone.Normal ? t.Accent : t.ToneColor(noticeTone);
            var r = new Rectangle(S(20), S(88), ClientSize.Width - S(40), NoticeBoxHeight);
            Theme.FillRound(g, t.Soft(c), r, S(8));
            string glyph = noticeTone == Tone.Good ? "" : noticeTone == Tone.Normal ? Glyphs.Sync : Glyphs.Warning;
            TextRenderer.DrawText(g, glyph, fNoticeIcon, new Rectangle(r.X + S(12), r.Y, S(20), S(40)), c,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, notice, fNotice, new Rectangle(r.X + S(40), r.Y + S(9), r.Width - S(52), r.Height - S(18)), t.Text,
                TextFormatFlags.Left | TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
        }

        TextRenderer.DrawText(g, "Closing this window keeps USB Ports in the tray. Right-click its tray icon for options or to exit.", fFoot,
            new Rectangle(S(20), list.Bottom + S(10), ClientSize.Width - S(40), S(22)), t.Muted, t.Back,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
    }
}
