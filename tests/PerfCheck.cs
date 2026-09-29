// The performance check: .\test.ps1 -Perf (see .claude/rules/performance.md).
// It measures the installed copy of USB Ports while it sits hidden in the tray:
// memory, threads, handles, GDI / USER objects, idle CPU, the growth over 30
// forced rescans, and the main window open. It sends the app only the messages
// performance.md lists and never changes a setting.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Win32;

static class PerfCheck
{
    // GetGuiResources flags (winuser.h; learn.microsoft.com .../nf-winuser-getguiresources)
    const uint GR_GDIOBJECTS = 0, GR_USEROBJECTS = 1;
    const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;   // Process security and access rights
    const int WM_DEVICECHANGE = 0x0219, DBT_DEVNODES_CHANGED = 0x0007;   // WinUser.h / Dbt.h
    const int WM_SYSCOMMAND = 0x0112, SC_CLOSE = 0xF060;                 // WinUser.h
    const int GWL_EXSTYLE = -20, WS_EX_TOOLWINDOW = 0x80;                // WinUser.h (the corner panel is a tool window)

    // Main window open (Ports page), measured 2026-09-30 on 1.7.1 (see RESULTS); budget = baseline + 25 %.
    static readonly double WindowOpenPrivateBaselineMB = 28.1;

    [DllImport("user32.dll")] static extern uint GetGuiResources(IntPtr process, uint flags);
    [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc cb, IntPtr p);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out int pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder s, int max);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int index);
    [DllImport("user32.dll")] static extern bool PostMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
    delegate bool EnumProc(IntPtr h, IntPtr p);

    class Snapshot { public double PrivateMB, WorkingSetMB; public int Threads, Handles, Gdi, User; public double CpuSeconds; }

    class Row { public string Measure, Value, Budget, Result; }

    static readonly List<Row> rows = new List<Row>();
    static bool failed;

    public static int Run()
    {
        string exe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\USB Ports\UsbPorts.exe");
        Process app = FindApp(exe);
        if (app == null) { Console.WriteLine("FAIL  The installed app is not running (" + exe + ")."); return 1; }
        IntPtr overlay;
        IntPtr main = MainWindow(app.Id, out overlay);
        if (main == IntPtr.Zero) { Console.WriteLine("FAIL  Could not find the app's main window."); return 1; }
        if (IsWindowVisible(main) || (overlay != IntPtr.Zero && IsWindowVisible(overlay)))
        {
            Console.WriteLine("FAIL  USB Ports has a window open. Close it (to the tray) and run -Perf again.");
            return 1;
        }

        Console.WriteLine("Measuring USB Ports " + app.MainModule.FileVersionInfo.FileVersion + " (pid " + app.Id + ") in the tray. This takes about 3.5 minutes.");
        Console.WriteLine("Letting it settle for 60 s...");
        Thread.Sleep(60000);
        Snapshot idle = Take(app);
        Console.WriteLine("Measuring idle CPU for 60 s...");
        Thread.Sleep(60000);
        Snapshot idleEnd = Take(app);

        Add("Private memory (Private Bytes)", idle.PrivateMB, 32, "MB");
        Add("Working set", idle.WorkingSetMB, 12, "MB");
        Add("CPU while idle (per minute)", idleEnd.CpuSeconds - idle.CpuSeconds, 0.05, "s");
        Add("Threads", idle.Threads, 8, "");
        Add("Handles", idle.Handles, 450, "");
        Add("GDI objects", idle.Gdi, 80, "");
        Add("USER objects", idle.User, 80, "");

        Console.WriteLine("Leak check: 30 forced rescans, 2 s apart...");
        for (int i = 0; i < 30; i++)
        {
            PostMessage(main, WM_DEVICECHANGE, (IntPtr)DBT_DEVNODES_CHANGED, IntPtr.Zero);
            Thread.Sleep(2000);
        }
        Thread.Sleep(10000);
        Snapshot after = Take(app);
        Add("Growth over 30 rescans: private", after.PrivateMB - idleEnd.PrivateMB, 2, "MB");
        Add("Growth over 30 rescans: handles", after.Handles - idleEnd.Handles, 10, "");
        Add("Growth over 30 rescans: GDI", after.Gdi - idleEnd.Gdi, 2, "");
        Add("Growth over 30 rescans: USER", after.User - idleEnd.User, 2, "");

        Console.WriteLine("Opening the main window for 5 s...");
        using (EventWaitHandle show = EventWaitHandle.OpenExisting(@"Local\UsbPortsViewer.Show")) show.Set();
        Thread.Sleep(5000);
        Snapshot open = Take(app);
        if (WindowOpenPrivateBaselineMB > 0) Add("Main window open: private", open.PrivateMB, WindowOpenPrivateBaselineMB * 1.25, "MB");
        else Report("Main window open: private", open.PrivateMB.ToString("0.0") + " MB", "(measure)", "-");
        Report("Main window open: working set", open.WorkingSetMB.ToString("0.0") + " MB", "-", "-");
        if (CloseToTray()) PostMessage(main, WM_SYSCOMMAND, (IntPtr)SC_CLOSE, IntPtr.Zero);   // like its X button: back to the tray
        else Console.WriteLine("Note: \"Keep running in the tray when closed\" is off, so the window was left open. Hide it by hand.");

        Print();
        return failed ? 1 : 0;
    }

    static Process FindApp(string exe)
    {
        foreach (Process p in Process.GetProcessesByName("UsbPorts"))
        {
            try { if (string.Equals(p.MainModule.FileName, exe, StringComparison.OrdinalIgnoreCase)) return p; }
            catch { }
        }
        return null;
    }

    // The main window is the non-tool window titled "USB Ports"; the corner panel is a tool window.
    static IntPtr MainWindow(int pid, out IntPtr overlay)
    {
        IntPtr found = IntPtr.Zero, panel = IntPtr.Zero;
        var title = new StringBuilder(64);
        EnumWindows(delegate (IntPtr h, IntPtr p)
        {
            int owner;
            GetWindowThreadProcessId(h, out owner);
            if (owner != pid) return true;
            title.Length = 0;
            GetWindowText(h, title, title.Capacity);
            if (title.ToString() != "USB Ports") return true;
            if ((GetWindowLong(h, GWL_EXSTYLE) & WS_EX_TOOLWINDOW) != 0) panel = h; else found = h;
            return true;
        }, IntPtr.Zero);
        overlay = panel;
        return found;
    }

    static Snapshot Take(Process app)
    {
        app.Refresh();
        var s = new Snapshot
        {
            PrivateMB = app.PrivateMemorySize64 / 1048576.0,
            WorkingSetMB = app.WorkingSet64 / 1048576.0,
            Threads = app.Threads.Count,
            Handles = app.HandleCount,
            CpuSeconds = app.TotalProcessorTime.TotalSeconds
        };
        IntPtr h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, app.Id);
        if (h != IntPtr.Zero)
        {
            s.Gdi = (int)GetGuiResources(h, GR_GDIOBJECTS);
            s.User = (int)GetGuiResources(h, GR_USEROBJECTS);
            CloseHandle(h);
        }
        return s;
    }

    static bool CloseToTray()
    {
        using (RegistryKey k = Registry.CurrentUser.OpenSubKey(@"Software\UsbPorts"))
        {
            object v = k == null ? null : k.GetValue("CloseToTray");
            return !(v is int) || (int)v != 0;   // on by default
        }
    }

    static void Add(string measure, double value, double limit, string unit)
    {
        bool ok = value <= limit;
        if (!ok) failed = true;
        string fmt = unit == "" ? "0" : unit == "s" ? "0.000" : "0.0";
        string u = unit == "" ? "" : " " + unit;
        Report(measure, value.ToString(fmt) + u, "<= " + limit.ToString(unit == "" ? "0" : "0.##") + u, ok ? "pass" : "FAIL");
    }

    static void Report(string measure, string value, string budget, string result)
    {
        rows.Add(new Row { Measure = measure, Value = value, Budget = budget, Result = result });
    }

    static void Print()
    {
        Console.WriteLine();
        Console.WriteLine("| Measure | Value | Budget | Result |");
        Console.WriteLine("|---|---|---|---|");
        foreach (Row r in rows) Console.WriteLine("| " + r.Measure + " | " + r.Value + " | " + r.Budget + " | " + r.Result + " |");
        Console.WriteLine();
        Console.WriteLine(failed ? "Summary: performance check FAILED" : "Summary: performance check passed");
    }
}
