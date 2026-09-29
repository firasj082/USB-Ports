// The test runner (see .claude/rules/testing.md). test.ps1 compiles src\*.cs and
// tests\*.cs into dist\UsbPorts.Tests.exe with /main:TestRunner and runs it.
//
//   (no switch)     unit tests
//   --integration   also the integration tests (real Windows, read-only)
//   --hardware      also the hardware tests (only after the owner confirms in chat)
//   --perf          the performance check against the installed, running app
//   --filter <text> only tests whose name contains the text
//
// A test is a public static void method with no parameters, marked [Unit],
// [Integration] or [Hardware]. Each one runs with its own empty test registry key
// (Software\UsbPorts.Test) and temp data folder, so the owner's settings and logs
// are never touched. The exit code is the number of failures.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

[AttributeUsage(AttributeTargets.Method)] class UnitAttribute : Attribute { }
[AttributeUsage(AttributeTargets.Method)] class IntegrationAttribute : Attribute { }
[AttributeUsage(AttributeTargets.Method)] class HardwareAttribute : Attribute { }

static class TestRunner
{
    // The current test's temp folder (also Settings.DataFolder while it runs).
    public static string TempDir;

    // The tests' output folder for images (dist\test-output).
    public static string OutputDir
    {
        get
        {
            string exeDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            string dir = Path.Combine(exeDir, "test-output");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    [STAThread]
    static int Main(string[] args)
    {
        int helper;
        if (TestHelpers.TryRun(args, out helper)) return helper;   // child-process modes used by some tests

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        if (Has(args, "--perf")) return PerfCheck.Run();

        bool integration = Has(args, "--integration"), hardware = Has(args, "--hardware");
        string filter = Value(args, "--filter");
        string root = Path.Combine(Path.GetTempPath(), "UsbPorts.Tests." + Process.GetCurrentProcess().Id);

        var tests = Find(integration, hardware, filter);
        int passed = 0, failed = 0, skipped = 0;
        var total = Stopwatch.StartNew();
        try
        {
            foreach (KeyValuePair<string, MethodInfo> t in tests)
            {
                TempDir = Path.Combine(root, t.Value.DeclaringType.Name + "." + t.Value.Name);
                Directory.CreateDirectory(TempDir);
                Settings.UseTestStorage(TempDir);
                Settings.DeleteTestStorage();   // every test starts from empty settings
                var sw = Stopwatch.StartNew();
                string outcome, detail = null;
                try
                {
                    t.Value.Invoke(null, null);
                    outcome = "PASS"; passed++;
                }
                catch (TargetInvocationException ex)
                {
                    Exception inner = ex.InnerException ?? ex;
                    if (inner is SkipTest) { outcome = "SKIP"; skipped++; detail = inner.Message; }
                    else
                    {
                        outcome = "FAIL"; failed++;
                        detail = inner is CheckFailed ? inner.Message : inner.ToString();
                    }
                }
                Console.WriteLine("{0}  {1,6} ms  {2}", outcome, sw.ElapsedMilliseconds, t.Key);
                if (detail != null) Console.WriteLine("        " + detail.Replace("\n", "\n        "));
            }
        }
        finally
        {
            Settings.DeleteTestStorage();
            try { Directory.Delete(root, true); } catch { }
        }
        Console.WriteLine();
        Console.WriteLine("Summary: {0} passed, {1} failed, {2} skipped ({3}) in {4:0.0} s",
            passed, failed, skipped, integration || hardware ? (hardware ? "unit, integration, hardware" : "unit, integration") : "unit",
            total.Elapsed.TotalSeconds);
        return failed;
    }

    // Unit tests first, then integration, then hardware; each group by class, then in source order.
    static List<KeyValuePair<string, MethodInfo>> Find(bool integration, bool hardware, string filter)
    {
        var found = new List<KeyValuePair<int, KeyValuePair<string, MethodInfo>>>();
        foreach (Type type in Assembly.GetExecutingAssembly().GetTypes())
        {
            foreach (MethodInfo m in type.GetMethods(BindingFlags.Public | BindingFlags.Static))
            {
                if (m.ReturnType != typeof(void) || m.GetParameters().Length > 0) continue;
                int group;
                if (m.IsDefined(typeof(UnitAttribute), false)) group = 0;
                else if (m.IsDefined(typeof(IntegrationAttribute), false)) { if (!integration) continue; group = 1; }
                else if (m.IsDefined(typeof(HardwareAttribute), false)) { if (!hardware) continue; group = 2; }
                else continue;
                string name = type.Name + "." + m.Name;
                if (filter != null && name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) continue;
                found.Add(new KeyValuePair<int, KeyValuePair<string, MethodInfo>>(group, new KeyValuePair<string, MethodInfo>(name, m)));
            }
        }
        found.Sort(delegate (KeyValuePair<int, KeyValuePair<string, MethodInfo>> a, KeyValuePair<int, KeyValuePair<string, MethodInfo>> b)
        {
            if (a.Key != b.Key) return a.Key.CompareTo(b.Key);
            int c = string.CompareOrdinal(a.Value.Value.DeclaringType.Name, b.Value.Value.DeclaringType.Name);
            return c != 0 ? c : a.Value.Value.MetadataToken.CompareTo(b.Value.Value.MetadataToken);
        });
        var list = new List<KeyValuePair<string, MethodInfo>>();
        foreach (var f in found) list.Add(f.Value);
        return list;
    }

    static bool Has(string[] args, string name)
    {
        return Array.Exists(args, delegate (string a) { return string.Equals(a, name, StringComparison.OrdinalIgnoreCase); });
    }

    static string Value(string[] args, string name)
    {
        for (int i = 0; i < args.Length - 1; i++)
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
        return null;
    }
}
