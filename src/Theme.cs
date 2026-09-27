// Colours, fonts and drawing helpers shared by every window.
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using Microsoft.Win32;

public enum Tone { Good, Normal, Warn, Bad, Empty }

public class Theme
{
    public bool Dark;
    public Color Back, Card, Border, Text, Muted, Hover, Accent, AccentHover;
    public Color Good = Color.FromArgb(22, 163, 74);
    public Color Warn = Color.FromArgb(217, 119, 6);
    public Color Bad = Color.FromArgb(220, 38, 38);
    public Color Normal;
    public string IconFont;

    // The app's theme: the dark / light choice from Settings, or Windows' own until one is made.
    public static Theme ForWindows()
    {
        return ShouldBeDark() ? Dark_() : Light();
    }

    public static bool ShouldBeDark()
    {
        int choice = Settings.DarkModeChoice;
        return choice >= 0 ? choice == 1 : WindowsIsDark();
    }

    public static bool WindowsIsDark()
    {
        try
        {
            object v = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1);
            return v is int && (int)v == 0;
        }
        catch { return false; }
    }

    // Switch this theme object in place; every window paints from it, so they
    // only need a repaint to follow.
    public void Apply(bool dark)
    {
        Theme s = dark ? Dark_() : Light();
        Dark = s.Dark; Back = s.Back; Card = s.Card; Border = s.Border; Text = s.Text; Muted = s.Muted;
        Hover = s.Hover; Accent = s.Accent; AccentHover = s.AccentHover; Normal = s.Normal;
        Good = s.Good; Warn = s.Warn; Bad = s.Bad;
    }

    public static Theme Light()
    {
        var t = new Theme();
        t.Back = Color.FromArgb(243, 243, 243);
        t.Card = Color.White;
        t.Border = Color.FromArgb(229, 229, 229);
        t.Text = Color.FromArgb(27, 27, 27);
        t.Muted = Color.FromArgb(104, 104, 104);
        t.Hover = Color.FromArgb(234, 234, 234);
        t.Accent = Color.FromArgb(0, 103, 192);
        t.AccentHover = Color.FromArgb(25, 117, 197);
        t.Normal = Color.FromArgb(100, 116, 139);
        t.IconFont = PickIconFont();
        return t;
    }

    public static Theme Dark_()
    {
        var t = new Theme();
        t.Dark = true;
        t.Back = Color.FromArgb(32, 32, 32);
        t.Card = Color.FromArgb(43, 43, 43);
        t.Border = Color.FromArgb(58, 58, 58);
        t.Text = Color.FromArgb(255, 255, 255);
        t.Muted = Color.FromArgb(170, 170, 170);
        t.Hover = Color.FromArgb(55, 55, 55);
        t.Accent = Color.FromArgb(76, 194, 255);
        t.AccentHover = Color.FromArgb(98, 205, 255);
        t.Normal = Color.FromArgb(82, 91, 107);
        t.Warn = Color.FromArgb(234, 138, 20);   // a touch brighter so text stays readable on dark cards
        t.Bad = Color.FromArgb(239, 68, 68);
        t.IconFont = PickIconFont();
        return t;
    }

    // The corner panel is always dark: it floats over whatever is on screen.
    public static Theme ForOverlay()
    {
        Theme t = Dark_();
        t.Back = Color.FromArgb(24, 24, 27);
        t.Card = Color.FromArgb(24, 24, 27);
        return t;
    }

    public Color ToneColor(Tone tone)
    {
        switch (tone)
        {
            case Tone.Good: return Good;
            case Tone.Warn: return Warn;
            case Tone.Bad: return Bad;
            case Tone.Empty: return Muted;
            default: return Dark ? Color.FromArgb(148, 163, 184) : Normal;
        }
    }

    public Color PillColor(Tone tone)
    {
        return tone == Tone.Normal ? Normal : ToneColor(tone);
    }

    public Color IconColor(Tone tone)
    {
        return tone == Tone.Normal ? Accent : ToneColor(tone);
    }

    public Color Soft(Color c)
    {
        return Blend(c, Card, Dark ? 0.22 : 0.12);
    }

    public static Color Blend(Color c, Color over, double amount)
    {
        return Color.FromArgb(
            (int)(over.R + (c.R - over.R) * amount),
            (int)(over.G + (c.G - over.G) * amount),
            (int)(over.B + (c.B - over.B) * amount));
    }

    static string iconFont;

    // Windows 11 has Segoe Fluent Icons, Windows 10 has Segoe MDL2 Assets (same code points).
    // One registry read instead of listing every installed font.
    static string PickIconFont()
    {
        if (iconFont != null) return iconFont;
        iconFont = "Segoe MDL2 Assets";
        try
        {
            if (Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Fonts", "Segoe Fluent Icons (TrueType)", null) != null)
                iconFont = "Segoe Fluent Icons";
        }
        catch { }
        return iconFont;
    }

    public static GraphicsPath Round(RectangleF r, float radius)
    {
        var p = new GraphicsPath();
        float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    // Height of wrapped text. Measures with the control's own drawing surface: the
    // no-surface overload wraps differently from what is drawn at some display scales.
    public static int WrappedHeight(System.Windows.Forms.Control c, string text, Font font, int width)
    {
        using (Graphics g = c.CreateGraphics())
            return System.Windows.Forms.TextRenderer.MeasureText(g, text, font, new Size(width, 0),
                System.Windows.Forms.TextFormatFlags.WordBreak | System.Windows.Forms.TextFormatFlags.NoPadding).Height;
    }

    public static void FillRound(Graphics g, Color c, RectangleF r, float radius)
    {
        using (var b = new SolidBrush(c))
        using (GraphicsPath p = Round(r, radius)) g.FillPath(b, p);
    }

    public static void StrokeRound(Graphics g, Color c, RectangleF r, float radius)
    {
        using (var pen = new Pen(c))
        using (GraphicsPath p = Round(r, radius)) g.DrawPath(pen, p);
    }
}

// Windows 11 touches: dark title bar and rounded corners.
static class Dwm
{
    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    [DllImport("user32.dll")]
    static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

    public static void DarkTitleBar(IntPtr hwnd, bool dark)
    {
        int v = dark ? 1 : 0;
        try
        {
            DwmSetWindowAttribute(hwnd, 20, ref v, 4);
            SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0004 | 0x0020);   // repaint the title bar now
        }
        catch { }
    }

    public static void RoundCorners(IntPtr hwnd)
    {
        int v = 2;
        try { DwmSetWindowAttribute(hwnd, 33, ref v, 4); } catch { }
    }
}

// Glyph code points shared by Segoe Fluent Icons and Segoe MDL2 Assets.
static class Glyphs
{
    public const string Usb = "";
    public const string Drive = "";
    public const string Mouse = "";
    public const string Keyboard = "";
    public const string Phone = "";
    public const string Camera = "";
    public const string Game = "";
    public const string Audio = "";
    public const string Network = "";
    public const string Printer = "";
    public const string Bluetooth = "";
    public const string Card = "";
    public const string Warning = "";
    public const string Refresh = "";
    public const string Pin = "";
    public const string Info = "";
    public const string Sync = "";
    public const string Copy = "";
    public const string Wifi = "";
    public const string Microphone = "";
    public const string Speaker = "";
    public const string Lock = "";
    public const string Fingerprint = "";
    public const string Pen = "";
    public const string Battery = "";
    public const string Monitor = "";
    public const string Download = "";
    public const string Driver = "";
    public const string Moon = "";
    public const string Sun = "";
    public const string Settings = "";
}
