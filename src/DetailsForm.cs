// The Details window for one device: everything DeviceDetails could find,
// grouped into sections, with a Copy button.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Threading;
using System.Windows.Forms;

class DetailsForm : Form, IThemed
{
    readonly Theme t;
    readonly float k;
    readonly PortReading reading;
    readonly Panel scroller;
    readonly DetailsView view;
    readonly FlatButton copy, close;
    readonly Font fTitle, fSub, fPill, fIcon;
    List<DetailSection> sections;

    public DetailsForm(Theme t, PortReading r, Icon icon)
    {
        this.t = t; reading = r;
        using (Graphics g = CreateGraphics()) k = g.DpiX / 96f;
        Text = r.Device.Name + " - Details";
        Icon = icon;
        BackColor = t.Back;
        ForeColor = t.Text;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        KeyPreview = true;
        DoubleBuffered = true;
        ClientSize = new Size(S(600), S(660));
        MinimumSize = new Size(S(480), S(360));

        fTitle = new Font("Segoe UI Semibold", 13f);
        fSub = new Font("Segoe UI", 9f);
        fPill = new Font("Segoe UI Semibold", 8.5f);
        fIcon = new Font(t.IconFont, 15f);

        scroller = new Panel { AutoScroll = true, BackColor = t.Back };
        view = new DetailsView(t, k);
        scroller.Controls.Add(view);
        copy = new FlatButton(t, k, Glyphs.Copy, "Copy details", false);
        close = new FlatButton(t, k, "", "Close", true);
        copy.Click += delegate { CopyAll(); };
        close.Click += delegate { Close(); };
        Controls.Add(scroller);
        Controls.Add(copy);
        Controls.Add(close);
        copy.Enabled = false;
        LayoutAll();

        ThreadPool.QueueUserWorkItem(delegate
        {
            List<DetailSection> found;
            try { found = DeviceDetails.Collect(reading); }
            catch (Exception ex)
            {
                found = new List<DetailSection>();
                var s = new DetailSection("Could not read details");
                s.Add("Error", ex.Message);
                found.Add(s);
            }
            try { BeginInvoke((MethodInvoker)delegate { sections = found; view.SetSections(found); copy.Enabled = true; LayoutAll(); }); } catch { }
        });
    }

    int S(float v) { return (int)Math.Round(v * k); }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Dwm.DarkTitleBar(Handle, t.Dark);
    }

    public void ApplyTheme()
    {
        BackColor = t.Back;
        ForeColor = t.Text;
        scroller.BackColor = t.Back;
        view.BackColor = t.Back;
        if (IsHandleCreated) Dwm.DarkTitleBar(Handle, t.Dark);
        Invalidate(true);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (scroller != null) LayoutAll();
        Invalidate();
    }

    void LayoutAll()
    {
        int header = S(92), footer = S(60);
        scroller.SetBounds(0, header, ClientSize.Width, Math.Max(S(40), ClientSize.Height - header - footer));
        int w = scroller.ClientSize.Width - S(40) - (scroller.VerticalScroll.Visible ? 0 : SystemInformation.VerticalScrollBarWidth);
        view.SetBounds(S(20), view.Top, Math.Max(S(200), w), view.Height);
        view.Relayout();
        close.Location = new Point(ClientSize.Width - S(20) - close.Width, ClientSize.Height - S(46));
        copy.Location = new Point(close.Left - S(8) - copy.Width, close.Top);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.Escape) Close();
        if (e.Control && e.KeyCode == Keys.C) CopyAll();
    }

    void CopyAll()
    {
        if (sections == null) return;
        try { Clipboard.SetText(DeviceDetails.AsText(reading.Device.Name, sections)); copy.Text = "Copied"; }
        catch { copy.Text = "Copy failed"; }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        Tone tone = Present.ToneOf(reading);
        Color tc = t.IconColor(tone);
        var ic = new Rectangle(S(20), S(22), S(48), S(48));
        using (var b = new SolidBrush(t.Soft(tc))) g.FillEllipse(b, ic);
        TextRenderer.DrawText(g, Present.Glyph(reading), fIcon, ic, tc, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

        int x = S(84);
        string pill = Present.Pill(reading);
        int right = ClientSize.Width - S(20);
        if (pill != null)
        {
            Size ps = TextRenderer.MeasureText(pill, fPill, Size.Empty, TextFormatFlags.NoPadding);
            var pr = new Rectangle(right - ps.Width - S(24), S(26), ps.Width + S(24), S(24));
            Color pc = t.PillColor(tone);
            Theme.FillRound(g, pc, pr, S(12));
            TextRenderer.DrawText(g, pill, fPill, pr, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            right = pr.Left - S(10);
        }
        TextRenderer.DrawText(g, reading.Device.Name, fTitle, new Rectangle(x, S(22), right - x, S(28)), t.Text, t.Back,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
        TextRenderer.DrawText(g, reading.Device.Kind + "   ·   Port " + reading.Port.Number, fSub, new Rectangle(x, S(50), ClientSize.Width - x - S(20), S(20)), t.Muted, t.Back,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
    }
}

// Draws the sections as cards of key / value rows. Long values wrap.
class DetailsView : Control
{
    readonly Theme t;
    readonly float k;
    readonly Font fSection, fKey, fValue;
    List<DetailSection> sections;
    const TextFormatFlags Wrap = TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl | TextFormatFlags.NoPadding;

    public DetailsView(Theme t, float k)
    {
        this.t = t; this.k = k;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = t.Back;
        fSection = new Font("Segoe UI Semibold", 10f);
        fKey = new Font("Segoe UI", 9f);
        fValue = new Font("Segoe UI", 9f);
        Height = S(60);
    }

    int S(float v) { return (int)Math.Round(v * k); }
    int KeyWidth { get { return S(190); } }
    int ValueWidth { get { return Math.Max(S(80), Width - KeyWidth - S(48)); } }

    public void SetSections(List<DetailSection> s) { sections = s; Relayout(); }

    public void Relayout()
    {
        Height = sections == null ? S(60) : Measure();
        Invalidate();
    }

    int RowHeight(KeyValuePair<string, string> row)
    {
        int kh, vh;
        using (Graphics g = CreateGraphics())
        {
            kh = TextRenderer.MeasureText(g, row.Key, fKey, new Size(KeyWidth - S(12), 0), Wrap).Height;
            vh = TextRenderer.MeasureText(g, row.Value, fValue, new Size(ValueWidth, 0), Wrap).Height;
        }
        return Math.Max(kh, vh) + S(14);
    }

    int Measure()
    {
        int y = 0;
        foreach (DetailSection s in sections)
        {
            y += S(30);
            foreach (KeyValuePair<string, string> row in s.Rows) y += RowHeight(row);
            y += S(16) + S(6);
        }
        return y + S(10);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.Clear(t.Back);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        if (sections == null)
        {
            TextRenderer.DrawText(g, "Reading device details...", fValue, ClientRectangle, t.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            return;
        }
        int y = 0;
        foreach (DetailSection s in sections)
        {
            TextRenderer.DrawText(g, s.Title, fSection, new Rectangle(0, y, Width, S(24)), t.Text, t.Back,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            y += S(30);
            int h = S(16);
            foreach (KeyValuePair<string, string> row in s.Rows) h += RowHeight(row);
            var card = new Rectangle(0, y, Width - 1, h - S(8));
            Theme.FillRound(g, t.Card, card, S(8));
            Theme.StrokeRound(g, t.Border, card, S(8));
            int ry = y + S(8);
            for (int i = 0; i < s.Rows.Count; i++)
            {
                KeyValuePair<string, string> row = s.Rows[i];
                int rh = RowHeight(row);
                if (i > 0)
                    using (var pen = new Pen(t.Border)) g.DrawLine(pen, card.X + S(16), ry, card.Right - S(16), ry);
                TextRenderer.DrawText(g, row.Key, fKey, new Rectangle(S(16), ry + S(7), KeyWidth - S(12), rh - S(14)), t.Muted, t.Card, Wrap);
                TextRenderer.DrawText(g, row.Value, fValue, new Rectangle(S(16) + KeyWidth, ry + S(7), ValueWidth, rh - S(14)), t.Text, t.Card, Wrap);
                ry += rh;
            }
            y += h + S(6);
        }
    }
}
