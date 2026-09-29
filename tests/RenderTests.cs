// Off-screen renders of the windows in both themes, saved to dist\test-output\ to be
// looked at. Nothing is shown on screen. Fixed, made-up readings keep the images stable.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;

static class RenderTests
{
    public static readonly DateTime When = new DateTime(2026, 9, 29, 10, 30, 0);

    public static List<PortReading> SampleReadings()
    {
        UsbDevice drive = Fake.Drive("USB\\VID_0781&PID_5581\\1", "SanDisk Ultra", "E:", 3);
        drive.Drives[0].Total = 16L * 1073741824; drive.Drives[0].Free = 5L * 1073741824;
        UsbDevice slow = Fake.Drive("USB\\VID_152D&PID_0578\\2", "Portable SSD", "F:", 2);
        slow.Usb3Capable = true;
        slow.Drives[0].Total = 512L * 1073741824; slow.Drives[0].Free = 120L * 1073741824;
        UsbDevice mouse = Fake.Device("USB\\VID_046D&PID_C08B\\3", "Gaming mouse", "Mouse", 1);
        mouse.DriverVersion = "10.0.26100.1"; mouse.DriverProvider = "Microsoft"; mouse.DriverUpdated = new DateTime(2026, 5, 2);
        return Fake.Readings(
            Fake.Reading(Fake.Port(1), mouse),
            Fake.Reading(Fake.Port(2), drive),
            Fake.Reading(Fake.Port(3), slow),
            new PortReading { Port = Fake.Port(4) });
    }

    // WinForms creates a form's child windows only when it is shown. This one is shown
    // fully transparent, without taking the focus, so nothing appears on screen.
    class InvisibleMainForm : MainForm
    {
        public InvisibleMainForm(Theme t, Icon icon) : base(t, icon) { ShowInTaskbar = false; Opacity = 0; }
        protected override bool ShowWithoutActivation { get { return true; } }
    }

    // Draws a window's client area, saves it, and checks it isn't blank.
    public static void Render(Form f, string name)
    {
        if (!f.Visible) { f.Show(); Application.DoEvents(); }
        using (var whole = new Bitmap(f.Width, f.Height))
        {
            f.DrawToBitmap(whole, new Rectangle(0, 0, f.Width, f.Height));
            Point client = f.PointToScreen(Point.Empty);
            var area = new Rectangle(client.X - f.Left, client.Y - f.Top, f.ClientSize.Width, f.ClientSize.Height);
            using (Bitmap bmp = whole.Clone(area, whole.PixelFormat))
            {
                bmp.Save(Path.Combine(TestRunner.OutputDir, name + ".png"), ImageFormat.Png);
                var colours = new HashSet<int>();
                for (int x = 0; x < bmp.Width; x += 7)
                    for (int y = 0; y < bmp.Height; y += 7) colours.Add(bmp.GetPixel(x, y).ToArgb());
                Check.True(colours.Count > 8, name + " has content (" + colours.Count + " colours sampled)");
            }
        }
    }

    static void Main(Theme t, MainForm.Page page, string name)
    {
        using (Icon icon = TrayApp.LoadIcon(32))
        using (var f = new InvisibleMainForm(t, icon))
        {
            f.ShowReadings(SampleReadings(), When);
            f.SetNotice("Reconnected. SanDisk Ultra is running at USB 3 (5 Gbps).", Tone.Good, true);
            if (page != MainForm.Page.Ports) f.ShowPage(page);
            Render(f, name);
            f.Close();
        }
    }

    [Integration]
    public static void MainWindowLight() { Main(Theme.Light(), MainForm.Page.Ports, "main-ports-light"); }

    [Integration]
    public static void MainWindowDark() { Main(Theme.Dark_(), MainForm.Page.Ports, "main-ports-dark"); }

    [Integration]
    public static void SettingsLight() { Main(Theme.Light(), MainForm.Page.Settings, "settings-light"); }

    [Integration]
    public static void SettingsDark() { Main(Theme.Dark_(), MainForm.Page.Settings, "settings-dark"); }

    // The corner panel has one look: always dark, over whatever is on screen.
    [Integration]
    public static void CornerPanel()
    {
        using (var menu = new ContextMenuStrip())
        using (var f = new OverlayForm(Theme.ForOverlay(), menu))
        {
            f.ShowReadings(SampleReadings(), When);
            f.Opacity = 0;   // it shows without taking the focus; keep it invisible too
            Render(f, "corner");
            f.Close();
        }
    }
}
