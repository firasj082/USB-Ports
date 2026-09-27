// Reconnect = the software version of unplugging and plugging a device back in.
// The hub switches the port off and on, so the device and port agree on a
// speed again (e.g. back to USB 3 after falling back to USB 2).
//
// Power-cycling a port needs administrator rights, so the app re-launches
// itself elevated with --reconnect; Windows shows its usual approval prompt.
// Drives are safely ejected first; if Windows refuses because something is
// using the drive, nothing is disconnected. It also refuses while any OTHER USB
// drive is attached, because the ports share power (checked twice: before
// anything happens, and again right before the port is switched off).
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32.SafeHandles;

static class Reconnect
{
    const uint IOCTL_USB_HUB_CYCLE_PORT = 0x220444;

    public const int Ok = 0, EjectRefused = 2, CycleFailed = 3, BadArguments = 4, OtherDrivesAttached = 5;

    // ---------------- UI side ----------------

    // Runs the elevated helper without blocking the UI; reports back on the UI thread.
    public static void Start(Control ui, UsbDevice d, Action<bool, string> done)
    {
        string result = Path.Combine(Path.GetTempPath(), "UsbPorts-reconnect-" + Guid.NewGuid().ToString("N") + ".txt");
        var args = new StringBuilder();
        args.Append("--reconnect \"").Append(d.HubPath).Append("\" ").Append(d.ConnectionIndex);
        if (d.InstanceId != null) args.Append(" --device \"").Append(d.InstanceId).Append('"');
        if (d.DiskInstanceIds.Count > 0 && d.InstanceId != null) args.Append(" --eject \"").Append(d.InstanceId).Append('"');
        if (!Settings.PortsSharePower) args.Append(" --allow-other-drives");
        args.Append(" --result \"").Append(result).Append('"');

        ThreadPool.QueueUserWorkItem(delegate
        {
            bool ok = false;
            string message;
            try
            {
                var psi = new ProcessStartInfo(Application.ExecutablePath, args.ToString()) { UseShellExecute = true, Verb = "runas" };
                using (Process p = Process.Start(psi))
                {
                    if (!p.WaitForExit(60000)) message = "Windows did not finish reconnecting within a minute.";
                    else
                    {
                        string text = File.Exists(result) ? File.ReadAllText(result).Trim() : "";
                        ok = p.ExitCode == Ok;
                        message = text.Length > 0 ? text : "Reconnect ended with code " + p.ExitCode + ".";
                    }
                }
            }
            catch (Win32Exception ex)
            {
                message = ex.NativeErrorCode == 1223 ? "Cancelled: administrator approval was declined." : ex.Message;
            }
            catch (Exception ex) { message = ex.Message; }
            try { if (File.Exists(result)) File.Delete(result); } catch { }
            try { ui.BeginInvoke((MethodInvoker)delegate { done(ok, message); }); } catch { }
        });
    }

    // ---------------- elevated helper ----------------

    public static int RunHelper(string[] args)
    {
        string hub = null, eject = null, result = null, device = null;
        bool allowOtherDrives = false;
        int port = 0;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--reconnect" && i + 2 < args.Length) { hub = args[++i]; int.TryParse(args[++i], out port); }
            else if (args[i] == "--eject" && i + 1 < args.Length) eject = args[++i];
            else if (args[i] == "--device" && i + 1 < args.Length) device = args[++i];
            else if (args[i] == "--result" && i + 1 < args.Length) result = args[++i];
            else if (args[i] == "--allow-other-drives") allowOtherDrives = true;   // "USB ports share power" is off
        }
        if (hub == null || port <= 0) return Finish(result, BadArguments, "Reconnect was started with missing details.");

        // The laptop's USB ports share power: switching one port off and on can
        // knock out a drive on another port mid-read or mid-write (it corrupted a
        // flash drive in testing). So never do it while another USB drive is attached.
        string blockers = allowOtherDrives ? null : OtherDrives(device);
        if (blockers != null) return Finish(result, OtherDrivesAttached, BlockedMessage(blockers));

        if (eject != null && Native.Locate(eject) != 0)
        {
            string problem = DriveEjector.TryEject(eject);
            if (problem != null)
                return Finish(result, EjectRefused,
                    "Windows could not safely eject the drive: " + problem +
                    ". Close any programs, windows or installs using the drive and try again. Nothing was disconnected.");
            Thread.Sleep(1500);   // let the eject settle before the port goes down
            blockers = allowOtherDrives ? null : OtherDrives(device);   // check again: something may have been plugged in meanwhile
            if (blockers != null)
                return Finish(result, OtherDrivesAttached, BlockedMessage(blockers) + " The drive you chose was already safely ejected; unplug it and plug it back in to use it again.");
        }

        using (SafeFileHandle h = Native.OpenHub(hub))
        {
            if (h == null) return Finish(result, CycleFailed, "Could not open the USB controller.");
            byte[] req = new byte[8];
            BitConverter.GetBytes(port).CopyTo(req, 0);
            int error;
            if (Native.Ioctl(h, IOCTL_USB_HUB_CYCLE_PORT, req, out error) == null)
                return Finish(result, CycleFailed, "The USB controller refused to power-cycle the port (Windows error " + error + ": " + new Win32Exception(error).Message + ")."
                    + (eject != null ? " The drive was ejected; unplug it and plug it back in to use it again." : ""));
        }
        return Finish(result, Ok, "The port was switched off and on again.");
    }

    // Names of attached USB drives other than the device being reconnected (null if none).
    public static string OtherDrives(string deviceInstanceId)
    {
        var names = new List<string>();
        foreach (UsbDevice d in DriveEjector.UsbDrives())
        {
            if (deviceInstanceId != null && string.Equals(d.InstanceId, deviceInstanceId, StringComparison.OrdinalIgnoreCase)) continue;
            var letters = new List<string>();
            foreach (DriveVolume v in d.Drives) letters.Add(v.Letter);
            names.Add(letters.Count > 0 ? d.Name + " (" + string.Join(", ", letters.ToArray()) + ")" : d.Name);
        }
        return names.Count > 0 ? string.Join(", ", names.ToArray()) : null;
    }

    public static string BlockedMessage(string blockers)
    {
        return "Reconnect was stopped to protect " + blockers + ". Your laptop's USB ports share power, so switching one port off and on can disrupt a drive on another port. " +
               "Safely remove the other USB drive first (or unplug it), then try again. Nothing was disconnected. (You can change this under Settings > USB ports share power.)";
    }

    static int Finish(string resultPath, int code, string message)
    {
        try { if (resultPath != null) File.WriteAllText(resultPath, message); } catch { }
        return code;
    }
}
