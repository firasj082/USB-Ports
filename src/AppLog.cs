// A short record of app starts, exits and errors (last 300 lines), so it is
// always possible to tell whether USB Ports started at sign-in and why it stopped.
// %LOCALAPPDATA%\USB Ports\app.log
using System;
using System.Collections.Generic;
using System.IO;

static class AppLog
{
    static readonly object gate = new object();

    public static string PathName { get { return Path.Combine(Settings.DataFolder, "app.log"); } }

    public static void Write(string line)
    {
        lock (gate)
        {
            try
            {
                var lines = new List<string>();
                if (File.Exists(PathName)) lines.AddRange(File.ReadAllLines(PathName));
                lines.Add(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + line);
                if (lines.Count > 300) lines.RemoveRange(0, lines.Count - 300);
                File.WriteAllLines(PathName, lines.ToArray());
            }
            catch { }
        }
    }
}
