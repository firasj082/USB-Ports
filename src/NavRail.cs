// The navigation bar down the left of the main window, Windows 11 style: the
// app logo, then Ports and Corner mode at the top, Dark / Light and Settings at
// the bottom. The current page gets an accent pill; hovering fades in a highlight.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

class NavRail : Control
{
    class Item
    {
        public string Id, Glyph, Label;
        public bool Bottom;
        public Rectangle Bounds;
        public float Hover;   // 0..1, animated
    }

    readonly Theme t;
    readonly float k;
    readonly List<Item> items = new List<Item>();
    readonly Font fIcon, fLabel, fLogo;
    readonly Timer anim = new Timer { Interval = 15 };
    Item hot;
    string selected;
    DateTime lastTick;

    public event Action<string> ItemClicked;

    public NavRail(Theme t, float k)
    {
        this.t = t; this.k = k;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        fIcon = new Font(t.IconFont, 13f);
        fLabel = new Font("Segoe UI", 7.75f);
        fLogo = new Font(t.IconFont, 12f);
        Width = S(76);
        anim.Tick += Step;
    }

    int S(float v) { return (int)Math.Round(v * k); }

    public void Add(string id, string glyph, string label, bool bottom)
    {
        items.Add(new Item { Id = id, Glyph = glyph, Label = label, Bottom = bottom });
        LayoutItems();
    }

    public void Set(string id, string glyph, string label)
    {
        foreach (Item i in items) if (i.Id == id) { i.Glyph = glyph; i.Label = label; }
        Invalidate();
    }

    public string Selected
    {
        set { selected = value; Invalidate(); }
    }

    void LayoutItems()
    {
        int y = S(78);
        foreach (Item i in items)
            if (!i.Bottom) { i.Bounds = new Rectangle(S(8), y, Width - S(16), S(56)); y += S(60); }
        int b = Height - S(10);
        for (int n = items.Count - 1; n >= 0; n--)
            if (items[n].Bottom) { b -= S(56); items[n].Bounds = new Rectangle(S(8), b, Width - S(16), S(56)); b -= S(4); }
    }

    protected override void OnResize(EventArgs e) { base.OnResize(e); LayoutItems(); Invalidate(); }

    Item HitTest(Point p)
    {
        foreach (Item i in items) if (i.Bounds.Contains(p)) return i;
        return null;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        Item now = HitTest(e.Location);
        Cursor = now != null ? Cursors.Hand : Cursors.Default;
        if (now != hot) { hot = now; Animate(); }
    }

    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hot = null; Animate(); }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        Item hit = e.Button == MouseButtons.Left ? HitTest(e.Location) : null;
        if (hit != null && ItemClicked != null) ItemClicked(hit.Id);
    }

    void Animate()
    {
        if (anim.Enabled) return;
        lastTick = DateTime.Now;
        anim.Start();
    }

    void Step(object sender, EventArgs e)
    {
        DateTime now = DateTime.Now;
        float step = (float)Math.Min(0.05, (now - lastTick).TotalSeconds) / 0.12f;
        lastTick = now;
        bool moving = false;
        foreach (Item i in items)
        {
            float target = i == hot ? 1f : 0f;
            i.Hover = i.Hover < target ? Math.Min(target, i.Hover + step) : Math.Max(target, i.Hover - step);
            if (i.Hover != target) moving = true;
        }
        Invalidate();
        if (!moving) anim.Stop();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) anim.Dispose();
        base.Dispose(disposing);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.Clear(t.Rail);
        g.SmoothingMode = SmoothingMode.AntiAlias;

        // logo
        var logo = new Rectangle((Width - S(38)) / 2, S(20), S(38), S(38));
        using (var grad = new LinearGradientBrush(logo, Color.FromArgb(37, 99, 235), Color.FromArgb(6, 182, 212), 45f))
        using (GraphicsPath p = Theme.Round(logo, S(10))) g.FillPath(grad, p);
        TextRenderer.DrawText(g, Glyphs.Usb, fLogo, logo, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

        foreach (Item i in items)
        {
            bool sel = i.Id == selected;
            Color fill = sel ? t.Soft(t.Accent) : Theme.Blend(t.Hover, t.Rail, i.Hover);
            if (sel || i.Hover > 0) Theme.FillRound(g, fill, i.Bounds, S(8));
            if (sel)
                Theme.FillRound(g, t.Accent, new RectangleF(i.Bounds.X - S(4), i.Bounds.Y + S(17), S(4), S(22)), S(2));
            Color fg = sel ? t.Accent : t.Text;
            TextRenderer.DrawText(g, i.Glyph, fIcon, new Rectangle(i.Bounds.X, i.Bounds.Y + S(7), i.Bounds.Width, S(24)), fg,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, i.Label, fLabel, new Rectangle(i.Bounds.X, i.Bounds.Y + S(33), i.Bounds.Width, S(16)), sel ? t.Accent : t.Muted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }
}
