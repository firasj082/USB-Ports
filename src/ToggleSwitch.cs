// An on/off switch in the Windows 11 style; the knob slides when flipped.
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

class ToggleSwitch : Control
{
    readonly Theme t;
    readonly Timer anim = new Timer { Interval = 15 };
    bool on, hover;
    float pos;   // 0 = off, 1 = on (animated)
    DateTime lastTick;

    public event EventHandler CheckedChanged;

    public ToggleSwitch(Theme t, float k)
    {
        this.t = t;
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
        g.Clear(t.Card);   // switches sit on cards
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var track = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        float r = track.Height / 2;
        Color onColor = hover ? t.AccentHover : t.Accent;
        Color offColor = hover ? t.Hover : t.Card;
        Theme.FillRound(g, Theme.Blend(onColor, offColor, pos), track, r);
        if (pos < 1f)
            using (var pen = new Pen(Color.FromArgb((int)(255 * (1 - pos)), t.Muted)))
            using (GraphicsPath p = Theme.Round(track, r)) g.DrawPath(pen, p);
        float d = Height * (0.54f + 0.10f * pos);
        float cx = Height / 2f + (Width - Height) * pos;
        Color knob = Theme.Blend(t.Dark ? Color.Black : Color.White, t.Muted, pos);
        using (var b = new SolidBrush(knob)) g.FillEllipse(b, cx - d / 2, Height / 2f - d / 2, d, d);
        if (Focused) using (var pen = new Pen(t.Text) { DashStyle = DashStyle.Dot }) g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
    }
}
