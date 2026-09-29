// Win32 calls the tests use.
using System;
using System.Runtime.InteropServices;

static class Win32
{
    [DllImport("user32.dll")]
    public static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, IntPtr l);

    [DllImport("user32.dll")]
    public static extern IntPtr GetShellWindow();

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr h, out int pid);
}
