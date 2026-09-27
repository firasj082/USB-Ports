// Corner mode: a small see-through panel that stays on top in a screen corner.
// Drag it anywhere; double-click it to open the full window; right-click for the menu.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

class OverlayForm : Form
{
    const double IdleOpacity = 0.82;
    readonly Theme t;
    readonly float k;
    readonly Font fHead, fName, fPill, fIcon, fNum;
    List<PortReading> data = new List<PortReading>();
    DateTime updated;
    bool movedByUser;
    Point dragStart;
    bool dragging;

    public event EventHandler OpenRequested;

    public OverlayForm(Theme t, ContextMenuStrip menu)
    {
        this.t = t;
        using (Graphics g = CreateGraphics()) k = g.DpiX / 96f;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = t.Back;
        DoubleBuffered = true;
        Opacity = IdleOpacity;
        ContextMenuStrip = menu;
        Text = "USB Ports";
        fHead = new Font("Segoe UI Semibold", 7.5f);
        fName = new Font("Segoe UI", 9f);
        fPill = new Font("Segoe UI Semibold", 7.5f);
        fIcon = new Font(t.IconFont, 10.5f);
        fNum = new Font("Segoe UI Semibold", 8f);
        Size = new Size(S(300), S(60));
    }

    int S(float v) { return (int)Math.Round(v * k); }

    protected override bool ShowWithoutActivation { get { return true; } }

    protected override CreateParams CreateParams
    {
        get
        {
            CreateParams cp = base.CreateParams;
            cp.ExStyle |= 0x80;   // WS_EX_TOOLWINDOW: no taskbar button, not in Alt+Tab
            return cp;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Dwm.RoundCorners(Handle);
    }

    public void ShowReadings(List<PortReading> readings, DateTime when)
    {
        data = readings;
        updated = when;
        int h = S(34) + Math.Max(1, data.Count) * S(30) + S(8);
        if (movedByUser) Height = h;
        else { Height = h; PlaceInCorner(); }
        Invalidate();
    }

    public void PlaceInCorner()
    {
        Rectangle wa = Screen.PrimaryScreen.WorkingArea;
        Location = new Point(wa.Right - Width - S(16), wa.Bottom - Height - S(16));
    }

    protected override void OnMouseEnter(EventArgs e) { Opacity = 1.0; base.OnMouseEnter(e); }

    protected override void OnMouseLeave(EventArgs e)
    {
        if (!Bounds.Contains(Cursor.Position)) Opacity = IdleOpacity;
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left) { dragging = true; dragStart = e.Location; }
        base.OnMouseDown(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (dragging && (Math.Abs(e.X - dragStart.X) > 2 || Math.Abs(e.Y - dragStart.Y) > 2))
        {
            Location = new Point(Left + e.X - dragStart.X, Top + e.Y - dragStart.Y);
            movedByUser = true;
        }
        base.OnMouseMove(e);
    }

    protected override void OnMouseUp(MouseEventArgs e) { dragging = false; base.OnMouseUp(e); }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left && OpenRequested != null) OpenRequested(this, EventArgs.Empty);
        base.OnMouseDoubleClick(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.Clear(t.Back);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        int pad = S(14);

        TextRenderer.DrawText(g, "USB PORTS", fHead, new Rectangle(pad, S(8), S(120), S(20)), t.Muted, t.Back,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        if (updated != DateTime.MinValue)
            TextRenderer.DrawText(g, updated.ToLongTimeString(), fHead, new Rectangle(Width - pad - S(120), S(8), S(120), S(20)), t.Muted, t.Back,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

        int y = S(32);
        foreach (PortReading r in data)
        {
            Tone tone = Present.ToneOf(r);
            TextRenderer.DrawText(g, r.Port.Number.ToString(), fNum, new Rectangle(pad, y, S(12), S(30)), t.Muted, t.Back,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, Present.Glyph(r), fIcon, new Rectangle(pad + S(16), y, S(22), S(30)),
                tone == Tone.Empty ? t.Muted : t.IconColor(tone), t.Back,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

            int nameRight = Width - pad;
            string pill = Present.ShortPill(r);
            if (pill != null)
            {
                Size ps = TextRenderer.MeasureText(pill, fPill, Size.Empty, TextFormatFlags.NoPadding);
                var pr = new Rectangle(Width - pad - ps.Width - S(16), y + S(5), ps.Width + S(16), S(20));
                Color pc = t.PillColor(tone);
                Theme.FillRound(g, pc, pr, S(10));
                TextRenderer.DrawText(g, pill, fPill, pr, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                nameRight = pr.Left - S(8);
            }
            Color nc = r.Device != null ? t.Text : r.Problem != null ? t.Bad : t.Muted;
            TextRenderer.DrawText(g, Present.ShortTitle(r), fName, new Rectangle(pad + S(44), y, nameRight - pad - S(44), S(30)), nc, t.Back,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
            y += S(30);
        }
    }
}
