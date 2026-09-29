// The main window: a navigation bar on the left (Ports, Corner mode, Dark / Light,
// Settings) and a content area with a page title, an optional status banner and a
// scrolling page - the port cards or the Settings page. The window can be resized.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

class MainForm : Form, IThemed
{
    public enum Page { Ports, Settings }

    readonly Theme t;
    readonly float k;
    readonly NavRail rail;
    readonly Panel body;
    readonly PortListView list;
    readonly SettingsView settingsView;
    readonly IconButton refresh;
    readonly ToolTip tips = new ToolTip { InitialDelay = 400 };
    readonly Font fTitle, fSub, fNotice, fNoticeIcon;
    readonly Timer noticeTimer;
    Page page = Page.Ports;
    string subtitle = "Scanning...";
    string notice;
    Tone noticeTone;

    public event EventHandler RefreshClicked;
    public event EventHandler CornerClicked;
    public event EventHandler ThemeClicked;
    public event Action<bool> DarkModeChanged;   // from the Settings page switch
    public event Action DevicesChanged;          // Windows says devices were added or removed
    public event Action<PortReading> DetailsClicked;
    public event Action<PortReading> ReconnectClicked;

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    static extern int SetWindowTheme(IntPtr hwnd, string app, string idList);

    public MainForm(Theme t, Icon icon)
    {
        this.t = t;
        using (Graphics g = CreateGraphics()) k = g.DpiX / 96f;
        Text = "USB Ports";
        Icon = icon;
        FormBorderStyle = FormBorderStyle.Sizable;
        StartPosition = FormStartPosition.CenterScreen;
        KeyPreview = true;
        DoubleBuffered = true;
        MinimumSize = new Size(S(640), S(460));

        fTitle = new Font("Segoe UI Semibold", 17f);
        fSub = new Font("Segoe UI", 9f);
        fNotice = new Font("Segoe UI", 9f);
        fNoticeIcon = new Font(t.IconFont, 10f);

        rail = new NavRail(t, k);
        rail.Add("ports", Glyphs.Home, "Ports", false);
        rail.Add("corner", Glyphs.Pin, "Corner", false);
        rail.Add("theme", Glyphs.Moon, "Dark", true);
        rail.Add("settings", Glyphs.Settings, "Settings", true);
        rail.Selected = "ports";
        rail.ItemClicked += OnRail;
        Controls.Add(rail);

        refresh = new IconButton(t, k, Glyphs.Refresh, true);
        refresh.Click += delegate { Raise(RefreshClicked); };
        tips.SetToolTip(refresh, "Refresh now (F5)");
        Controls.Add(refresh);

        body = new Panel { AutoScroll = true };
        body.AutoScrollMargin = new Size(0, S(24));
        body.HandleCreated += delegate { SetWindowTheme(body.Handle, t.Dark ? "DarkMode_Explorer" : "Explorer", null); };   // scrollbar colours
        list = new PortListView(t, k);
        list.DetailsClicked += delegate (PortReading r) { if (DetailsClicked != null) DetailsClicked(r); };
        list.ReconnectClicked += delegate (PortReading r) { if (ReconnectClicked != null) ReconnectClicked(r); };
        settingsView = new SettingsView(t, k) { Visible = false };
        settingsView.DarkModeChanged += delegate (bool on) { if (DarkModeChanged != null) DarkModeChanged(on); };
        body.Controls.Add(list);
        body.Controls.Add(settingsView);
        body.ClientSizeChanged += delegate { LayoutContent(); };   // also fires when the scrollbar appears
        Controls.Add(body);

        noticeTimer = new Timer { Interval = 30000 };
        noticeTimer.Tick += delegate { noticeTimer.Stop(); SetNotice(null, Tone.Normal, false); };

        Rectangle wa = Screen.PrimaryScreen.WorkingArea;
        ClientSize = new Size(Math.Min(S(820), wa.Width - S(40)), Math.Min(S(720), wa.Height - S(60)));
        ApplyTheme();
        LayoutAll();
    }

    int S(float v) { return (int)Math.Round(v * k); }

    void Raise(EventHandler h) { if (h != null) h(this, EventArgs.Empty); }

    void OnRail(string id)
    {
        if (id == "ports") ShowPage(Page.Ports);
        else if (id == "settings") ShowPage(Page.Settings);
        else if (id == "corner") Raise(CornerClicked);
        else if (id == "theme") Raise(ThemeClicked);
    }

    public void ShowPage(Page p)
    {
        page = p;
        rail.Selected = p == Page.Ports ? "ports" : "settings";
        list.Visible = p == Page.Ports;
        settingsView.Visible = p == Page.Settings;
        refresh.Visible = p == Page.Ports;
        if (p == Page.Settings) settingsView.Reload();
        body.AutoScrollPosition = Point.Empty;
        LayoutAll();
    }

    public void ApplyTheme()
    {
        BackColor = t.Back;
        ForeColor = t.Text;
        body.BackColor = t.Back;
        list.BackColor = t.Back;
        settingsView.BackColor = t.Back;
        rail.Set("theme", t.Dark ? Glyphs.Sun : Glyphs.Moon, t.Dark ? "Light" : "Dark");
        if (IsHandleCreated) Dwm.DarkTitleBar(Handle, t.Dark);
        if (body.IsHandleCreated) SetWindowTheme(body.Handle, t.Dark ? "DarkMode_Explorer" : "Explorer", null);   // scrollbar colours
        if (page == Page.Settings) settingsView.Reload();
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

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (body != null) LayoutAll();
    }

    int ContentLeft { get { return rail.Width + S(28); } }

    int NoticeBoxHeight
    {
        get
        {
            if (notice == null || page != Page.Ports) return 0;
            int textWidth = ClientSize.Width - ContentLeft - S(28) - S(52);
            int h = Theme.WrappedHeight(this, notice, fNotice, Math.Max(S(100), textWidth));
            return Math.Max(S(40), h + S(18));
        }
    }

    int HeaderHeight
    {
        get
        {
            int n = NoticeBoxHeight;
            return S(92) + (n > 0 ? n + S(12) : 0);
        }
    }

    void LayoutAll()
    {
        rail.SetBounds(0, 0, rail.Width, ClientSize.Height);
        refresh.Location = new Point(ClientSize.Width - S(28) - refresh.Width, S(26));
        body.SetBounds(rail.Width, HeaderHeight, Math.Max(0, ClientSize.Width - rail.Width), Math.Max(0, ClientSize.Height - HeaderHeight));
        LayoutContent();
        Invalidate();
    }

    bool layingOut;

    // Page content fills the scrolling area, with side margins.
    void LayoutContent()
    {
        if (layingOut) return;
        layingOut = true;
        try
        {
            int w = Math.Max(S(300), body.ClientSize.Width - S(28) - S(28));
            int top = S(2) + body.AutoScrollPosition.Y;
            if (list.Visible) list.SetBounds(S(28), top, w, list.Height);
            if (settingsView.Visible) settingsView.SetBounds(S(28), top, w, settingsView.Height);
            body.PerformLayout();   // WinForms never shrinks the scroll range on its own
        }
        finally { layingOut = false; }
    }

    public void ShowReadings(List<PortReading> readings, DateTime when)
    {
        list.SetData(readings);
        int devices = 0;
        foreach (PortReading r in readings) if (r.Device != null) devices++;
        subtitle = readings.Count + (readings.Count == 1 ? " port" : " ports") + "   ·   " + devices + (devices == 1 ? " device" : " devices") +
                   "   ·   updated " + when.ToShortTimeString();
        LayoutContent();
        if (page == Page.Ports) Invalidate(new Rectangle(ContentLeft, 0, ClientSize.Width, S(80)));
    }

    public void SetBusy(bool busy) { refresh.Spinning = busy; }

    public void SetBusyPort(int port) { list.SetBusyPort(port); }

    // A coloured status line under the title. Sticky notices stay until replaced.
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
        if (e.KeyCode == Keys.F5 && page == Page.Ports) Raise(RefreshClicked);
    }

    // ---------------- Windows shutting down ----------------

    [DllImport("user32.dll")]
    static extern int GetSystemMetrics(int index);

    public bool ShuttingDown;
    public event Action ShutdownEnding;   // raised once the shutdown or restart is definite

    protected override void WndProc(ref Message m)
    {
        const int WM_DEVICECHANGE = 0x219, DBT_DEVNODES_CHANGED = 7, SM_SHUTTINGDOWN = 0x2000;
        if (m.Msg == WM_DEVICECHANGE && m.WParam.ToInt64() == DBT_DEVNODES_CHANGED && DevicesChanged != null) DevicesChanged();
        if (m.Msg == SessionEnd.WM_QUERYENDSESSION || m.Msg == SessionEnd.WM_ENDSESSION)
        {
            // Sign-out, shutdown or restart: see SessionEnd.
            long flags = m.LParam.ToInt64();
            SessionEnd.Result r = SessionEnd.Decide(m.Msg, m.WParam != IntPtr.Zero, flags, GetSystemMetrics(SM_SHUTTINGDOWN) != 0,
                DriveEjector.ShutdownRequestedRecently);
            if (r.Ending)
                AppLog.Write("Session ending (" + (m.Msg == SessionEnd.WM_QUERYENDSESSION ? "asked" : "confirmed") + ", flags 0x" + flags.ToString("X") +
                             "): treated as " + r.What + ".");
            if (r.Shutdown && r.Ending) ShuttingDown = true;
            if (!r.Ending) ShuttingDown = false;   // the shutdown was cancelled
            // Act before WinForms starts closing windows, so the app is still alive to do it.
            if (r.RaiseShutdownEnding && ShutdownEnding != null) ShutdownEnding();
        }
        base.WndProc(ref m);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        int x = ContentLeft;
        string title = page == Page.Ports ? "USB Ports" : "Settings";
        string sub = page == Page.Ports ? subtitle : "Changes apply straight away.";
        TextRenderer.DrawText(g, title, fTitle, new Point(x - S(2), S(20)), t.Text, t.Back, TextFormatFlags.NoPadding);
        TextRenderer.DrawText(g, sub, fSub, new Point(x, S(56)), t.Muted, t.Back, TextFormatFlags.NoPadding);

        if (notice != null && page == Page.Ports)
        {
            Color c = noticeTone == Tone.Normal ? t.Accent : t.ToneColor(noticeTone);
            var r = new Rectangle(x, S(92), ClientSize.Width - x - S(28), NoticeBoxHeight);
            Theme.FillRound(g, t.Soft(c), r, S(8));
            string glyph = noticeTone == Tone.Good ? "" : noticeTone == Tone.Normal ? Glyphs.Sync : Glyphs.Warning;
            TextRenderer.DrawText(g, glyph, fNoticeIcon, new Rectangle(r.X + S(12), r.Y, S(20), S(40)), c,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, notice, fNotice, new Rectangle(r.X + S(40), r.Y + S(9), r.Width - S(52), r.Height - S(18)), t.Text,
                TextFormatFlags.Left | TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
        }
    }
}
