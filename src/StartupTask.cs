// Start with Windows, the reliable way: a Task Scheduler task that runs at sign-in
// for the current user (no admin rights needed). On some PCs Explorer reads the
// Run entry at sign-in but never starts it; Task Scheduler does not depend on that.
//
// The task does NOT run USB Ports itself. Task Scheduler ends the programs it
// started when you sign out - and a shutdown signs you out first - so a copy it
// started would be gone before Windows asks apps to shut down, and could never
// eject drives at shutdown. Instead the task asks Explorer to open a shortcut, so
// USB Ports runs as an ordinary app under Explorer, and the task itself ends at once.
//
// The Run entry is kept so the app still shows in Task Manager > Startup apps, and
// either launch exits straight away if Start with Windows is off (or disabled there).
using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Principal;

static class StartupTask
{
    const string TaskName = "USB Ports";

    static string ShortcutFor(string exe) { return Path.Combine(Path.GetDirectoryName(exe), "USB Ports (sign-in).lnk"); }

    static string Explorer { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"); } }

    public static void Register(string exe)
    {
        string shortcut = ShortcutFor(exe);
        Installer.CreateShortcut(shortcut, exe, "--tray --autostart");
        string user = SecurityElement.Escape(WindowsIdentity.GetCurrent().Name);
        string xml =
            "<?xml version=\"1.0\" encoding=\"UTF-16\"?>" +
            "<Task version=\"1.2\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">" +
            "<RegistrationInfo><Description>Starts USB Ports quietly in the system tray when you sign in.</Description></RegistrationInfo>" +
            "<Triggers><LogonTrigger><Enabled>true</Enabled><UserId>" + user + "</UserId><Delay>PT10S</Delay></LogonTrigger></Triggers>" +
            "<Principals><Principal id=\"Author\"><UserId>" + user + "</UserId><LogonType>InteractiveToken</LogonType><RunLevel>LeastPrivilege</RunLevel></Principal></Principals>" +
            "<Settings><MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy><DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>" +
            "<StopIfGoingOnBatteries>false</StopIfGoingOnBatteries><ExecutionTimeLimit>PT5M</ExecutionTimeLimit><Enabled>true</Enabled></Settings>" +
            "<Actions Context=\"Author\"><Exec><Command>" + SecurityElement.Escape(Explorer) + "</Command>" +
            "<Arguments>" + SecurityElement.Escape("\"" + shortcut + "\"") + "</Arguments></Exec></Actions></Task>";
        WithRootFolder(delegate (object folder)
        {
            // 6 = create or update, 3 = run with the signed-in user's own token
            Call(folder, "RegisterTask", TaskName, xml, 6, null, null, 3, null);
        });
    }

    public static void Remove()
    {
        WithRootFolder(delegate (object folder)
        {
            try { Call(folder, "DeleteTask", TaskName, 0); } catch { }   // already gone
        });
    }

    // True if the task exists in its current form (opening this exe's sign-in shortcut).
    public static bool IsRegisteredFor(string exe)
    {
        string shortcut = ShortcutFor(exe);
        if (!File.Exists(shortcut)) return false;
        bool found = false;
        WithRootFolder(delegate (object folder)
        {
            try
            {
                object task = Call(folder, "GetTask", TaskName);
                string xml = (string)task.GetType().InvokeMember("Xml", BindingFlags.GetProperty, null, task, null);
                found = xml.IndexOf(SecurityElement.Escape(shortcut), StringComparison.OrdinalIgnoreCase) >= 0 &&
                        xml.IndexOf("explorer.exe", StringComparison.OrdinalIgnoreCase) >= 0;
                Marshal.FinalReleaseComObject(task);
            }
            catch { found = false; }
        });
        return found;
    }

    static void WithRootFolder(Action<object> work)
    {
        object service = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service"));
        try
        {
            Call(service, "Connect");
            object folder = Call(service, "GetFolder", "\\");
            try { work(folder); }
            finally { Marshal.FinalReleaseComObject(folder); }
        }
        finally { Marshal.FinalReleaseComObject(service); }
    }

    static object Call(object target, string method, params object[] args)
    {
        return target.GetType().InvokeMember(method, BindingFlags.InvokeMethod, null, target, args);
    }
}
