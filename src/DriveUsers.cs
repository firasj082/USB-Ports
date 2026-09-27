// Finds the programs that are using a drive (running from it, or holding a
// file open on it) and closes them, so the drive can be ejected at shutdown.
// Only programs running under the current user can be seen or closed; Windows
// services and system processes are out of reach, and USB Ports never closes itself.
// This only runs when an eject at shutdown was refused, so it costs nothing otherwise.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

static class DriveUsers
{
    const int SystemExtendedHandleInformation = 0x40;
    const uint STATUS_INFO_LENGTH_MISMATCH = 0xC0000004;
    const uint PROCESS_DUP_HANDLE = 0x0040, PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    const uint DUPLICATE_SAME_ACCESS = 0x2;
    const uint FILE_TYPE_DISK = 1;

    [DllImport("ntdll.dll")]
    static extern uint NtQuerySystemInformation(int infoClass, IntPtr buffer, int length, out int needed);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll")]
    static extern bool CloseHandle(IntPtr h);
    [DllImport("kernel32.dll")]
    static extern IntPtr GetCurrentProcess();
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool DuplicateHandle(IntPtr srcProcess, IntPtr src, IntPtr dstProcess, out IntPtr dst, uint access, bool inherit, uint options);
    [DllImport("kernel32.dll")]
    static extern uint GetFileType(IntPtr h);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    static extern uint GetFinalPathNameByHandle(IntPtr h, StringBuilder path, uint length, uint flags);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    static extern bool QueryFullProcessImageName(IntPtr process, uint flags, StringBuilder path, ref int size);
    [DllImport("user32.dll")]
    static extern bool EnumWindows(EnumWindowsProc cb, IntPtr p);
    [DllImport("user32.dll")]
    static extern uint GetWindowThreadProcessId(IntPtr h, out int pid);
    [DllImport("user32.dll")]
    static extern bool PostMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
    delegate bool EnumWindowsProc(IntPtr h, IntPtr p);

    // Never touch these, even if they show up.
    static readonly string[] untouchable = { "system", "smss", "csrss", "wininit", "winlogon", "services", "lsass", "svchost", "dwm", "fontdrvhost", "sihost", "ctfmon" };

    // pid -> program name, for everything found using any of the given drive letters ("D:").
    public static Dictionary<int, string> Find(List<string> letters, TimeSpan budget)
    {
        var found = new Dictionary<int, string>();
        if (letters.Count == 0) return found;
        int me = Process.GetCurrentProcess().Id;

        // 1. programs whose .exe lives on the drive (a game or app launched from it)
        foreach (Process p in Process.GetProcesses())
        {
            try
            {
                if (p.Id == me || p.Id <= 4) continue;
                string exe = ImagePath(p.Id);
                if (exe != null && OnDrives(exe, letters)) found[p.Id] = p.ProcessName;
            }
            catch { }
            finally { p.Dispose(); }
        }

        // 2. programs holding a file open on the drive. Run on a worker thread with
        //    a time limit so an unusual handle can never stall the shutdown.
        var open = new Dictionary<int, string>();
        var worker = new Thread(delegate () { try { FindOpenFiles(letters, me, open); } catch { } }) { IsBackground = true };
        worker.Start();
        if (worker.Join(budget)) lock (open) foreach (KeyValuePair<int, string> kv in open) found[kv.Key] = kv.Value;

        foreach (int pid in new List<int>(found.Keys))
            if (Array.IndexOf(untouchable, found[pid].ToLowerInvariant()) >= 0) found.Remove(pid);
        return found;
    }

    static bool OnDrives(string path, List<string> letters)
    {
        if (path.StartsWith(@"\\?\")) path = path.Substring(4);
        foreach (string l in letters) if (path.StartsWith(l, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    static string ImagePath(int pid)
    {
        IntPtr h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (h == IntPtr.Zero) return null;
        try
        {
            var sb = new StringBuilder(1024);
            int size = sb.Capacity;
            return QueryFullProcessImageName(h, 0, sb, ref size) ? sb.ToString() : null;
        }
        finally { CloseHandle(h); }
    }

    static void FindOpenFiles(List<string> letters, int me, Dictionary<int, string> open)
    {
        int fileType = FileHandleTypeIndex(me);
        if (fileType < 0) return;
        IntPtr buffer = SnapshotHandles();
        if (buffer == IntPtr.Zero) return;
        var processes = new Dictionary<int, IntPtr>();
        try
        {
            long count = Marshal.ReadIntPtr(buffer).ToInt64();
            int entrySize = IntPtr.Size == 8 ? 40 : 28;
            IntPtr first = IntPtr.Add(buffer, IntPtr.Size * 2);
            var sb = new StringBuilder(1024);
            for (long i = 0; i < count; i++)
            {
                IntPtr e = IntPtr.Add(first, (int)(i * entrySize));
                int pid = (int)Marshal.ReadIntPtr(e, IntPtr.Size).ToInt64();
                int type = Marshal.ReadInt16(e, IntPtr.Size * 3 + 6) & 0xFFFF;
                if (type != fileType || pid == me || pid <= 4) continue;
                lock (open) if (open.ContainsKey(pid)) continue;

                IntPtr proc;
                if (!processes.TryGetValue(pid, out proc))
                {
                    proc = OpenProcess(PROCESS_DUP_HANDLE, false, pid);   // fails for other users / protected processes
                    processes[pid] = proc;
                }
                if (proc == IntPtr.Zero) continue;

                IntPtr dup;
                if (!DuplicateHandle(proc, Marshal.ReadIntPtr(e, IntPtr.Size * 2), GetCurrentProcess(), out dup, 0, false, DUPLICATE_SAME_ACCESS)) continue;
                try
                {
                    if (GetFileType(dup) != FILE_TYPE_DISK) continue;   // skip pipes and devices
                    sb.Length = 0;
                    if (GetFinalPathNameByHandle(dup, sb, (uint)sb.Capacity, 0) == 0) continue;
                    if (!OnDrives(sb.ToString(), letters)) continue;
                    string name;
                    try { using (Process p = Process.GetProcessById(pid)) name = p.ProcessName; } catch { name = "process " + pid; }
                    lock (open) open[pid] = name;
                }
                finally { CloseHandle(dup); }
            }
        }
        finally
        {
            foreach (IntPtr p in processes.Values) if (p != IntPtr.Zero) CloseHandle(p);
            Marshal.FreeHGlobal(buffer);
        }
    }

    // The kernel's numeric type for "File" handles differs between Windows
    // versions; learn it from a file handle this app opens itself.
    static int FileHandleTypeIndex(int me)
    {
        using (FileStream own = File.OpenRead(Process.GetCurrentProcess().MainModule.FileName))
        {
            long mine = own.SafeFileHandle.DangerousGetHandle().ToInt64();
            IntPtr buffer = SnapshotHandles();
            if (buffer == IntPtr.Zero) return -1;
            try
            {
                long count = Marshal.ReadIntPtr(buffer).ToInt64();
                int entrySize = IntPtr.Size == 8 ? 40 : 28;
                IntPtr first = IntPtr.Add(buffer, IntPtr.Size * 2);
                for (long i = 0; i < count; i++)
                {
                    IntPtr e = IntPtr.Add(first, (int)(i * entrySize));
                    if ((int)Marshal.ReadIntPtr(e, IntPtr.Size).ToInt64() != me) continue;
                    if (Marshal.ReadIntPtr(e, IntPtr.Size * 2).ToInt64() == mine) return Marshal.ReadInt16(e, IntPtr.Size * 3 + 6) & 0xFFFF;
                }
                return -1;
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }
    }

    static IntPtr SnapshotHandles()
    {
        int size = 1 << 20;
        for (int attempt = 0; attempt < 8; attempt++)
        {
            IntPtr buffer = Marshal.AllocHGlobal(size);
            int needed;
            uint status = NtQuerySystemInformation(SystemExtendedHandleInformation, buffer, size, out needed);
            if (status == 0) return buffer;
            Marshal.FreeHGlobal(buffer);
            if (status != STATUS_INFO_LENGTH_MISMATCH) return IntPtr.Zero;
            size = Math.Max(size * 2, needed + (1 << 16));
        }
        return IntPtr.Zero;
    }

    // Ask each program to close (like clicking its X); force it if it's still
    // running after the grace period. Returns the names of what was closed.
    public static List<string> Close(Dictionary<int, string> programs, TimeSpan grace)
    {
        var closed = new List<string>();
        var running = new List<Process>();
        foreach (KeyValuePair<int, string> kv in programs)
        {
            try { running.Add(Process.GetProcessById(kv.Key)); } catch { }
        }
        var pids = new HashSet<int>(programs.Keys);
        EnumWindows(delegate (IntPtr h, IntPtr p)
        {
            int pid;
            GetWindowThreadProcessId(h, out pid);
            if (pids.Contains(pid)) PostMessage(h, 0x0010 /*WM_CLOSE*/, IntPtr.Zero, IntPtr.Zero);
            return true;
        }, IntPtr.Zero);

        DateTime deadline = DateTime.Now + grace;
        foreach (Process p in running)
        {
            try
            {
                int left = (int)Math.Max(0, (deadline - DateTime.Now).TotalMilliseconds);
                if (!p.WaitForExit(left)) { p.Kill(); p.WaitForExit(2000); }
                closed.Add(programs[p.Id]);
            }
            catch { }
            finally { p.Dispose(); }
        }
        return closed;
    }
}
