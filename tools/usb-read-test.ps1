# Read-only speed and stability test for a USB drive. It never writes anything,
# never opens the raw disk, and opens files with full sharing so no other program is
# blocked. Reads bypass Windows' file cache, so every byte really comes over the USB
# link: the speed it measures is the link's real speed, whatever any app reports.
# Meanwhile it watches Windows' logs for disconnects, I/O retries and resets.
#
#   .\tools\usb-read-test.ps1                    # D:, 30 seconds
#   .\tools\usb-read-test.ps1 -Seconds 600       # 10-minute soak (wiggle the cable meanwhile)
#   .\tools\usb-read-test.ps1 -Drive E: -MinFileMB 64
#
# Reading a result: USB 2 tops out near 40 MB/s in practice; a SATA SSD over a
# USB 3 (5 Gbps) link reads roughly 300-450 MB/s. Close games and launchers that use
# the drive first, so they neither slow the test nor write while the link is tested.
# Each run appends a summary line to dist\drive-tests.log in this repo. (Not AppData: when
# Claude's tools run this, Windows redirects AppData writes into the Claude app's package.)
param([string]$Drive = 'D:', [int]$Seconds = 30, [int]$MinFileMB = 256)
$ErrorActionPreference = 'Stop'
$Drive = $Drive.TrimEnd('\').ToUpper()

if (-not ('UsbReadTest.Reader' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace UsbReadTest
{
    public class Reader : IDisposable
    {
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr sa, uint disposition, uint flags, IntPtr template);
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool ReadFile(SafeFileHandle h, IntPtr buffer, int toRead, out int read, IntPtr overlapped);
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern IntPtr VirtualAlloc(IntPtr address, UIntPtr size, uint type, uint protect);
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool VirtualFree(IntPtr address, UIntPtr size, uint type);

        const uint GENERIC_READ = 0x80000000;       // read access only
        const uint SHARE_ALL = 7;                   // FILE_SHARE_READ | WRITE | DELETE
        const uint OPEN_EXISTING = 3;
        const uint NO_BUFFERING = 0x20000000;       // FILE_FLAG_NO_BUFFERING: bypass the cache
        const uint SEQUENTIAL_SCAN = 0x08000000;
        const int Block = 1 << 20;                  // 1 MiB: a multiple of any sector size

        readonly string[] files;
        readonly IntPtr buffer;
        int index = -1;
        SafeFileHandle handle;

        public long ElapsedMs;       // how long the last ReadFor really took
        public long SlowestReadMs;   // the longest single 1 MiB read in it
        public bool Failed;          // a read or open failed in it
        public int Error;            // the Win32 error of that failure

        public Reader(string[] files)
        {
            this.files = files;
            buffer = VirtualAlloc(IntPtr.Zero, (UIntPtr)Block, 0x3000, 0x04);   // page-aligned, as unbuffered reads require
            if (buffer == IntPtr.Zero) throw new OutOfMemoryException();
        }

        // Reads for about `ms` milliseconds, cycling through the files; returns the bytes read.
        public long ReadFor(int ms)
        {
            var sw = Stopwatch.StartNew();
            long total = 0;
            SlowestReadMs = 0;
            Failed = false;
            while (sw.ElapsedMilliseconds < ms)
            {
                if (handle == null && !OpenNext()) break;
                var one = Stopwatch.StartNew();
                int read;
                bool ok = ReadFile(handle, buffer, Block, out read, IntPtr.Zero);
                if (one.ElapsedMilliseconds > SlowestReadMs) SlowestReadMs = one.ElapsedMilliseconds;
                if (!ok) { Failed = true; Error = Marshal.GetLastWin32Error(); Close(); break; }
                if (read == 0) { Close(); continue; }   // end of this file: on to the next
                total += read;
            }
            ElapsedMs = sw.ElapsedMilliseconds;
            return total;
        }

        bool OpenNext()
        {
            for (int tries = 0; tries < files.Length; tries++)
            {
                index = (index + 1) % files.Length;
                SafeFileHandle h = CreateFileW(files[index], GENERIC_READ, SHARE_ALL, IntPtr.Zero, OPEN_EXISTING, NO_BUFFERING | SEQUENTIAL_SCAN, IntPtr.Zero);
                if (!h.IsInvalid) { handle = h; return true; }
                Error = Marshal.GetLastWin32Error();
                h.Dispose();
            }
            Failed = true;
            return false;
        }

        void Close() { if (handle != null) { handle.Dispose(); handle = null; } }

        public void Dispose() { Close(); VirtualFree(buffer, UIntPtr.Zero, 0x8000); }
    }
}
'@
}

# ---- what to read: existing big files (reading only; nothing is changed) ----
$link = & (Join-Path $PSScriptRoot 'usb-link-check.ps1') -Drive $Drive -Quiet
"Drive $Drive = disk $($link.Disk), $($link.Model)"
"Link (from Windows' device tree): lane $($link.Lane) " + $(if ($link.Usb3) { "(USB 3)" } elseif ($link.Lane) { "(USB 2)" } else { "" }) +
    $(if ($link.Hubs) { ", through $($link.Hubs)" } else { ", no hub" }) + ", protocol $($link.Protocol)"

$files = New-Object System.Collections.Generic.List[string]
$dirs = New-Object System.Collections.Generic.Stack[string]
$dirs.Push($Drive + '\')
$walk = [Diagnostics.Stopwatch]::StartNew()
$bytesFound = 0L
while ($dirs.Count -and $files.Count -lt 40 -and $walk.Elapsed.TotalSeconds -lt 20) {
    $info = New-Object IO.DirectoryInfo $dirs.Pop()
    try { foreach ($f in $info.GetFiles()) { if ($f.Length -ge $MinFileMB * 1MB) { $files.Add($f.FullName); $bytesFound += $f.Length } } } catch { }
    try {
        foreach ($d in $info.GetDirectories()) {
            if ($d.Attributes -band [IO.FileAttributes]::ReparsePoint) { continue }
            if ($d.Name -in 'System Volume Information', '$RECYCLE.BIN') { continue }
            $dirs.Push($d.FullName)
        }
    } catch { }
}
if ($files.Count -eq 0) { throw "No files of $MinFileMB MB or more found on $Drive. Try -MinFileMB 32." }
"Reading $($files.Count) existing files ($([math]::Round($bytesFound / 1GB, 1)) GB), cache bypassed, for $Seconds s."
""

# ---- Windows' own record of trouble on the drive ----
$seen = @{}
function NewEvents($since) {
    $list = @()
    $list += @(Get-WinEvent -FilterHashtable @{ LogName = 'System'; StartTime = $since } -ErrorAction SilentlyContinue |
        Where-Object { $_.ProviderName -match 'disk|ntfs|partmgr|uasp|usbstor|usbhub|usbxhci|storport' })
    $list += @(Get-WinEvent -FilterHashtable @{ LogName = 'Microsoft-Windows-Partition/Diagnostic'; Id = 1006; StartTime = $since } -ErrorAction SilentlyContinue)
    # Which device fell off the bus (the drive itself, or a hub in front of it)
    $list += @(Get-WinEvent -FilterHashtable @{ LogName = 'Microsoft-Windows-Kernel-PnP/Device Management'; Id = 1010; StartTime = $since } -ErrorAction SilentlyContinue |
        Where-Object { $_.Message -match 'Device (USB|SCSI)\\' })
    foreach ($e in ($list | Sort-Object TimeCreated)) {
        $key = $e.LogName + $e.RecordId
        if ($seen.ContainsKey($key)) { continue }
        $seen[$key] = $true
        $e
    }
}
function DeviceName($instanceId) {
    try { $n = (Get-PnpDeviceProperty -InstanceId $instanceId -KeyName 'DEVPKEY_NAME' -ErrorAction Stop).Data } catch { $n = $null }
    if ($n) { $n } else { 'unknown device' }
}
function Describe($e) {
    if ($e.Id -eq 1006 -and $e.LogName -like '*Partition*') {
        $x = [xml]$e.ToXml()
        $cap = ($x.Event.EventData.Data | Where-Object Name -eq 'Capacity').'#text'
        $model = ($x.Event.EventData.Data | Where-Object Name -eq 'Model').'#text'
        if ($cap -eq '0') { return "DISK REMOVED ($model)" } else { return "disk arrived ($model)" }
    }
    if ($e.Id -eq 1010 -and $e.Message -match 'Device (\S+) has been surprise removed') {
        return "DROPPED OFF THE USB BUS: " + (DeviceName $Matches[1]) + "  ($($Matches[1]))"
    }
    return "{0} {1}: {2}" -f $e.ProviderName, $e.Id, (($e.Message -split "`n")[0]).Trim()
}

$start = Get-Date
$every = if ($Seconds -le 60) { 1 } else { 10 }
$reader = [UsbReadTest.Reader]::new([string[]]$files.ToArray())
$good = New-Object System.Collections.Generic.List[double]
$errors = 0; $stalls = 0; $slowest = 0L
$intervalBytes = 0L; $intervalMs = 0L; $intervalMin = [double]::MaxValue
$events = @()
try {
    for ($s = 1; $s -le $Seconds; $s++) {
        $bytes = $reader.ReadFor(1000)
        $ms = [math]::Max(1, $reader.ElapsedMs)
        $rate = ($bytes / 1MB) / ($ms / 1000.0)
        if ($reader.SlowestReadMs -gt $slowest) { $slowest = $reader.SlowestReadMs }
        if ($reader.Failed) {
            $errors++
            "  {0,4}s  READ ERROR: {1} (Win32 {2})" -f $s, (New-Object ComponentModel.Win32Exception $reader.Error).Message, $reader.Error
            if ($ms -lt 1000) { Start-Sleep -Milliseconds (1000 - $ms) }   # the drive may be gone: wait for it
        } else {
            $good.Add($rate)
            if ($rate -lt 1) { $stalls++ }
        }
        $intervalBytes += $bytes; $intervalMs += [math]::Max($ms, 1000); if ($rate -lt $intervalMin) { $intervalMin = $rate }
        if ($s % $every -eq 0) {
            $avg = ($intervalBytes / 1MB) / ($intervalMs / 1000.0)
            if ($every -eq 1) { "  {0,4}s  {3:HH:mm:ss}  {1,7:N1} MB/s   slowest 1 MiB read {2} ms" -f $s, $avg, $reader.SlowestReadMs, (Get-Date) }
            else { "  {0,4}s  {1,7:N1} MB/s average, lowest second {2:N1} MB/s" -f $s, $avg, $intervalMin }
            $intervalBytes = 0L; $intervalMs = 0L; $intervalMin = [double]::MaxValue
        }
        if ($s % 5 -eq 0 -or $s -eq $Seconds) {
            foreach ($e in NewEvents $start) { $events += $e; "  {0,4}s  EVENT {1:HH:mm:ss}  {2}" -f $s, $e.TimeCreated, (Describe $e) }
        }
    }
}
finally { $reader.Dispose() }
Start-Sleep -Seconds 2   # let late log entries land
foreach ($e in NewEvents $start) { $events += $e; "  late  EVENT {0:HH:mm:ss}  {1}" -f $e.TimeCreated, (Describe $e) }

# ---- summary ----
$sorted = @($good | Sort-Object)
$median = if ($sorted.Count) { $sorted[[int][math]::Floor($sorted.Count / 2)] } else { 0 }
$removed = @($events | Where-Object { $_.LogName -like '*Partition*' -and (Describe $_) -like 'DISK REMOVED*' }).Count
$retries = @($events | Where-Object { $_.ProviderName -eq 'disk' -and $_.Id -eq 153 }).Count
$resets = @($events | Where-Object { $_.Id -eq 129 }).Count
$fsIssues = @($events | Where-Object { $_.ProviderName -match 'ntfs' -and $_.Id -in 55, 137, 140 }).Count
""
"Summary for $Drive over $Seconds s:"
"  Speed      : median {0:N1} MB/s, lowest second {1:N1} MB/s, slowest single read {2} ms" -f $median, $(if ($sorted.Count) { $sorted[0] } else { 0 }), $slowest
"  Speed says : " + $(if ($median -ge 60) { "USB 3 link (USB 2 cannot exceed ~40 MB/s)" }
    elseif ($median -ge 45) { "faster than USB 2 allows, but slow for USB 3" }
    else { "USB 2 speed (or a very slow drive): USB 2 tops out near 40 MB/s" })
"  Stability  : $removed disconnect(s), $errors second(s) with read errors, $stalls stalled second(s), $retries I/O retries (disk 153), $resets reset(s) (129), $fsIssues file-system warning(s)"
$dropped = @($events | Where-Object { $_.Id -eq 1010 } | ForEach-Object { (Describe $_) -replace '^DROPPED OFF THE USB BUS: ', '' } | Select-Object -Unique)
if ($dropped.Count) {
    "  Dropped    : " + ($dropped -join '; ')
    "               (a hub listed here = the hub's own link to the PC failed, not just the drive behind it)"
}
if ($removed + $errors + $stalls + $retries + $resets + $fsIssues -eq 0) { "  Verdict    : stable for the whole test." }
else { "  Verdict    : the link had problems during the test (details above)." }

try {
    $log = Join-Path (Split-Path $PSScriptRoot) 'dist\drive-tests.log'
    New-Item -ItemType Directory -Force (Split-Path $log) | Out-Null
    $line = "{0:yyyy-MM-dd HH:mm}  {1} lane {2}{3}  {4}s  median {5:N1} MB/s  disconnects {6}  read-errors {7}  stalls {8}  retries {9}  resets {10}" -f
        $start, $Drive, $link.Lane, $(if ($link.Hubs) { " via hub" } else { "" }), $Seconds, $median, $removed, $errors, $stalls, $retries, $resets
    Add-Content -Path $log -Value $line -Encoding UTF8
    "  Logged to  : $log"
} catch { }
