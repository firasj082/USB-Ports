// Shown when USB-Ports-Setup.exe is opened from anywhere other than the
// install folder: Install / Update, or Run without installing.
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

class SetupForm : Form
{
    public enum Choice { None, Install, Portable }

    readonly Theme t;
    readonly float k;
    readonly Font fTitle, fText, fOption, fBadge;
    readonly ToggleSwitch desktop, startup;
    readonly FlatButton install, portable;
    readonly bool update;

    public Choice Result = Choice.None;
    public bool DesktopShortcut { get { return desktop.Checked; } }
    public bool StartWithWindows { get { return startup.Checked; } }

    public SetupForm(Theme t, Icon icon)
    {
        this.t = t;
        update = Installer.IsInstalled;
        using (Graphics g = CreateGraphics()) k = g.DpiX / 96f;
        Text = update ? "Update USB Ports" : "Install USB Ports";
        Icon = icon;
        BackColor = t.Back;
        ForeColor = t.Text;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        DoubleBuffered = true;
        fTitle = new Font("Segoe UI Semibold", 15f);
        fText = new Font("Segoe UI", 9f);
        fOption = new Font("Segoe UI Semibold", 9.5f);
        fBadge = new Font(t.IconFont, 16f);

        desktop = new ToggleSwitch(t, k) { Checked = true };
        startup = new ToggleSwitch(t, k) { Checked = true };
        install = new FlatButton(t, k, "", update ? "Update" : "Install", true);
        portable = new FlatButton(t, k, "", "Run without installing", false);
        install.Click += delegate { Result = Choice.Install; Close(); };
        portable.Click += delegate { Result = Choice.Portable; Close(); };
        foreach (Control c in new Control[] { desktop, startup, install, portable }) Controls.Add(c);
        ActiveControl = install;

        int w = S(500);
        desktop.Location = new Point(w - S(36) - desktop.Width, S(176));
        startup.Location = new Point(w - S(36) - startup.Width, S(214));
        install.Location = new Point(w - S(24) - install.Width, S(290));
        portable.Location = new Point(install.Left - S(8) - portable.Width, S(290));
        ClientSize = new Size(w, S(340));
    }

    int S(float v) { return (int)Math.Round(v * k); }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Dwm.DarkTitleBar(Handle, t.Dark);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        int w = ClientSize.Width;

        var badge = new Rectangle(S(24), S(24), S(52), S(52));
        using (var grad = new LinearGradientBrush(badge, Color.FromArgb(37, 99, 235), Color.FromArgb(6, 182, 212), 45f))
        using (GraphicsPath p = Theme.Round(badge, S(13))) g.FillPath(grad, p);
        TextRenderer.DrawText(g, Glyphs.Usb, fBadge, badge, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

        TextRenderer.DrawText(g, update ? "Update USB Ports" : "Install USB Ports", fTitle, new Point(S(92), S(26)), t.Text, t.Back, TextFormatFlags.NoPadding);
        TextRenderer.DrawText(g, "See what is plugged into each USB port and how fast it runs, and safely eject USB drives at shutdown.",
            fText, new Rectangle(S(93), S(58), w - S(93) - S(24), S(40)), t.Muted, t.Back, TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);

        var card = new Rectangle(S(24), S(160), w - S(48), S(92));
        Theme.FillRound(g, t.Card, card, S(8));
        Theme.StrokeRound(g, t.Border, card, S(8));
        TextRenderer.DrawText(g, "Desktop shortcut", fOption, new Rectangle(card.X + S(16), S(172), S(300), S(30)), t.Text, t.Card, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        TextRenderer.DrawText(g, "Start with Windows (quietly, in the tray)", fOption, new Rectangle(card.X + S(16), S(210), S(320), S(30)), t.Text, t.Card, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

        TextRenderer.DrawText(g, "Installs for your account only, in " + Installer.InstallDir.Replace(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "%LOCALAPPDATA%") +
            ". No administrator rights needed. Uninstall any time from Settings > Apps.", fText,
            new Rectangle(S(24), S(106), w - S(48), S(44)), t.Muted, t.Back, TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
    }
}
