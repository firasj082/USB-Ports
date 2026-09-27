// Custom-drawn controls: a rounded button and the list of port cards.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

class FlatButton : Control
{
    readonly Theme t;
    readonly float k;
    readonly bool primary;
    readonly string glyph;
    readonly Font glyphFont;
    bool hover;

    public FlatButton(Theme t, float k, string glyph, string text, bool primary)
    {
        this.t = t; this.k = k; this.glyph = glyph; this.primary = primary;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Font = new Font("Segoe UI Semibold", 9f);
        glyphFont = new Font(t.IconFont, 9.5f);
        Cursor = Cursors.Hand;
        Text = text;
        Fit();
    }

    int S(float v) { return (int)Math.Round(v * k); }

    public void Fit()
    {
        Size text = TextRenderer.MeasureText(Text, Font, Size.Empty, TextFormatFlags.NoPadding);
        Size = new Size(S(14) + S(22) + text.Width + S(14), S(32));
    }

    protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); Fit(); Invalidate(); }
    protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }
    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.Clear(Parent != null ? Parent.BackColor : t.Back);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        Color fill, fg;
        if (primary)
        {
            fill = !Enabled ? Theme.Blend(t.Accent, t.Back, 0.5) : hover ? t.AccentHover : t.Accent;
            fg = t.Dark ? Color.Black : Color.White;
        }
        else
        {
            fill = hover && Enabled ? t.Hover : t.Card;
            fg = Enabled ? t.Text : t.Muted;
        }
        Theme.FillRound(g, fill, r, S(6));
        if (!primary) Theme.StrokeRound(g, t.Border, r, S(6));
        TextRenderer.DrawText(g, glyph, glyphFont, new Rectangle(S(12), 0, S(20), Height), fg,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        TextRenderer.DrawText(g, Text, Font, new Rectangle(S(36), 0, Width - S(36) - S(12), Height), fg,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }
}

// Windows that can follow a live dark / light switch.
interface IThemed { void ApplyTheme(); }

// A square icon button with a tooltip. Animations: background fades in on
// hover, the icon can turn on hover (settings gear) or spin while busy
// (refresh). The animation timer only runs while something is moving.
class IconButton : Control
{
    readonly Theme t;
    readonly float k;
    readonly bool primary;
    readonly Font glyphFont;
    readonly Timer anim = new Timer { Interval = 15 };
    string glyph;
    bool hot, spinning;
    float hover, turn, angle;
    DateTime lastTick;

    public bool TurnOnHover;

    public IconButton(Theme t, float k, string glyph, bool primary)
    {
        this.t = t; this.k = k; this.glyph = glyph; this.primary = primary;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        glyphFont = new Font(t.IconFont, 11f);
        Size = new Size((int)Math.Round(38 * k), (int)Math.Round(38 * k));
        Cursor = Cursors.Hand;
        anim.Tick += Step;
    }

    public string Glyph { set { glyph = value; Invalidate(); } }

    // Spins while busy, then finishes the turn it is on so it never stops crooked.
    public bool Spinning
    {
        get { return spinning; }
        set { spinning = value; if (value) Animate(); }
    }

    void Animate()
    {
        if (anim.Enabled) return;
        lastTick = DateTime.Now;
        anim.Start();
    }

    static float Toward(float v, float target, float step)
    {
        return v < target ? Math.Min(target, v + step) : Math.Max(target, v - step);
    }

    void Step(object sender, EventArgs e)
    {
        DateTime now = DateTime.Now;
        float dt = (float)Math.Min(0.05, (now - lastTick).TotalSeconds);
        lastTick = now;
        hover = Toward(hover, hot ? 1f : 0f, dt / 0.15f);
        turn = Toward(turn, TurnOnHover && hot ? 90f : 0f, dt * 450f);
        if (spinning || angle > 0)
        {
            angle += dt * 600f;
            if (angle >= 360f) angle = spinning ? angle - 360f : 0f;
        }
        Invalidate();
        if (!spinning && angle == 0 && hover == (hot ? 1f : 0f) && turn == (TurnOnHover && hot ? 90f : 0f)) anim.Stop();
    }

    protected override void OnMouseEnter(EventArgs e) { hot = true; Animate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hot = false; Animate(); base.OnMouseLeave(e); }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { anim.Dispose(); glyphFont.Dispose(); }
        base.Dispose(disposing);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.Clear(Parent != null ? Parent.BackColor : t.Back);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        float radius = 9 * k;
        Color fg;
        if (primary)
        {
            Theme.FillRound(g, Theme.Blend(t.AccentHover, t.Accent, hover), r, radius);
            fg = t.Dark ? Color.Black : Color.White;
        }
        else
        {
            Theme.FillRound(g, Theme.Blend(t.Hover, t.Card, hover), r, radius);
            Theme.StrokeRound(g, t.Border, r, radius);
            fg = t.Text;
        }
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        g.TranslateTransform(Width / 2f, Height / 2f);
        g.RotateTransform(angle + turn);
        using (var brush = new SolidBrush(fg))
        using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            g.DrawString(glyph, glyphFont, brush, new RectangleF(-Width / 2f, -Height / 2f + 1, Width, Height), sf);
        g.ResetTransform();
    }
}

// One card per port. Cards with a device get "Details" and "Reconnect" buttons.
class PortListView : Control
{
    class Target { public Rectangle Bounds; public PortReading Reading; public string Action; public bool Enabled; }

    readonly Theme t;
    readonly float k;
    readonly Font fCaption, fName, fDetail, fPill, fIcon, fIconSmall, fButton;
    readonly List<Target> targets = new List<Target>();
    List<PortReading> data = new List<PortReading>();
    Target hover;
    int busyPort;

    public event Action<PortReading> DetailsClicked;
    public event Action<PortReading> ReconnectClicked;

    public PortListView(Theme t, float k)
    {
        this.t = t; this.k = k;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = t.Back;
        fCaption = new Font("Segoe UI Semibold", 8.25f);
        fName = new Font("Segoe UI Semibold", 11.5f);
        fDetail = new Font("Segoe UI", 9f);
        fPill = new Font("Segoe UI Semibold", 8.5f);
        fButton = new Font("Segoe UI Semibold", 8.5f);
        fIcon = new Font(t.IconFont, 15f);
        fIconSmall = new Font(t.IconFont, 9f);
    }

    int S(float v) { return (int)Math.Round(v * k); }

    public void SetData(List<PortReading> readings)
    {
        data = readings;
        int h = 0;
        for (int i = 0; i < data.Count; i++) h += CardHeight(data[i]) + (i > 0 ? S(10) : 0);
        Height = Math.Max(h, S(60));
        Invalidate();
    }

    // The port whose Reconnect is running (0 = none): its button shows progress.
    public void SetBusyPort(int port) { busyPort = port; Invalidate(); }

    int CardHeight(PortReading r)
    {
        int lines = Present.Details(r).Count;
        int buttons = r.Device != null ? S(40) : 0;
        return Math.Max(S(76), S(62) + lines * S(20) + buttons + S(12));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.Clear(t.Back);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        targets.Clear();
        if (data.Count == 0)
        {
            TextRenderer.DrawText(g, "Looking for USB ports...", fDetail, ClientRectangle, t.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            return;
        }
        int y = 0;
        foreach (PortReading r in data)
        {
            int h = CardHeight(r);
            DrawCard(g, r, new Rectangle(0, y, Width - 1, h));
            y += h + S(10);
        }
    }

    void DrawCard(Graphics g, PortReading r, Rectangle card)
    {
        Theme.FillRound(g, t.Card, card, S(8));
        Theme.StrokeRound(g, t.Border, card, S(8));

        Tone tone = Present.ToneOf(r);
        Color toneColor = t.IconColor(tone);

        // icon in a soft circle
        var ic = new Rectangle(card.X + S(16), card.Y + S(18), S(40), S(40));
        Color circle = tone == Tone.Empty ? t.Hover : t.Soft(toneColor);
        using (var b = new SolidBrush(circle)) g.FillEllipse(b, ic);
        TextRenderer.DrawText(g, Present.Glyph(r), fIcon, ic, tone == Tone.Empty ? t.Muted : toneColor,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

        int x = card.X + S(72);
        int right = card.Right - S(16);

        TextRenderer.DrawText(g, "PORT " + r.Port.Number + "   ·   " + Present.PortKind(r.Port).ToUpperInvariant(), fCaption,
            new Rectangle(x, card.Y + S(14), right - x, S(18)), t.Muted, t.Card,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);

        // speed pill on the name row
        int nameRight = right;
        string pill = Present.Pill(r);
        if (pill != null)
        {
            Size ps = TextRenderer.MeasureText(pill, fPill, Size.Empty, TextFormatFlags.NoPadding);
            var pr = new Rectangle(right - ps.Width - S(24), card.Y + S(33), ps.Width + S(24), S(24));
            Color pc = t.PillColor(tone);
            Theme.FillRound(g, pc, pr, S(12));
            TextRenderer.DrawText(g, pill, fPill, pr, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            nameRight = pr.Left - S(10);
        }

        Color nameColor = r.Device != null ? t.Text : r.Problem != null ? t.Bad : t.Muted;
        TextRenderer.DrawText(g, Present.Title(r), fName, new Rectangle(x, card.Y + S(32), nameRight - x, S(26)), nameColor, t.Card,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);

        int ly = card.Y + S(62);
        foreach (InfoLine line in Present.Details(r))
        {
            Color c = line.Tone == Tone.Normal ? t.Muted : t.ToneColor(line.Tone);
            int lx = x;
            if (line.Glyph != null)
            {
                TextRenderer.DrawText(g, line.Glyph, fIconSmall, new Rectangle(lx, ly, S(16), S(20)), c, t.Card,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                lx += S(22);
            }
            TextRenderer.DrawText(g, line.Text, fDetail, new Rectangle(lx, ly, right - lx, S(20)), c, t.Card,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
            ly += S(20);
        }

        if (r.Device != null)
        {
            bool busy = busyPort == r.Port.Number;
            int bx = DrawButton(g, r, "details", Glyphs.Info, "Details", x, ly + S(8), true);
            DrawButton(g, r, "reconnect", Glyphs.Sync, busy ? "Reconnecting..." : "Reconnect", bx + S(8), ly + S(8), !busy && busyPort == 0);
        }
    }

    int DrawButton(Graphics g, PortReading r, string action, string glyph, string text, int x, int y, bool enabled)
    {
        Size ts = TextRenderer.MeasureText(text, fButton, Size.Empty, TextFormatFlags.NoPadding);
        var rect = new Rectangle(x, y, S(10) + S(18) + ts.Width + S(12), S(28));
        bool hot = enabled && hover != null && hover.Reading == r && hover.Action == action;
        Theme.FillRound(g, hot ? t.Hover : t.Card, rect, S(6));
        Theme.StrokeRound(g, t.Border, rect, S(6));
        Color fg = enabled ? t.Text : t.Muted;
        TextRenderer.DrawText(g, glyph, fIconSmall, new Rectangle(rect.X + S(8), rect.Y, S(18), rect.Height), enabled ? t.Accent : t.Muted,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        TextRenderer.DrawText(g, text, fButton, new Rectangle(rect.X + S(28), rect.Y, rect.Width - S(30), rect.Height), fg,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        targets.Add(new Target { Bounds = rect, Reading = r, Action = action, Enabled = enabled });
        return rect.Right;
    }

    Target HitTest(Point p)
    {
        foreach (Target x in targets) if (x.Enabled && x.Bounds.Contains(p)) return x;
        return null;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        Target now = HitTest(e.Location);
        bool changed = (now == null) != (hover == null) || (now != null && hover != null && (now.Reading != hover.Reading || now.Action != hover.Action));
        hover = now;
        Cursor = now != null ? Cursors.Hand : Cursors.Default;
        if (changed) Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (hover != null) { hover = null; Invalidate(); }
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        if (e.Button != MouseButtons.Left) return;
        Target hit = HitTest(e.Location);
        if (hit == null) return;
        if (hit.Action == "details" && DetailsClicked != null) DetailsClicked(hit.Reading);
        if (hit.Action == "reconnect" && ReconnectClicked != null) ReconnectClicked(hit.Reading);
    }
}
