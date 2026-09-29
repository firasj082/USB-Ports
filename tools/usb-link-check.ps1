# Which USB link is a drive really using? Independent of USB Ports' own scanner:
# follows Windows' Plug and Play tree from the drive up to the USB controller and
# reads Windows' own properties (location paths, drivers). Read-only; needs no admin.
#
#   .\tools\usb-link-check.ps1            # drive D:
#   .\tools\usb-link-check.ps1 -Drive E:
#   .\tools\usb-link-check.ps1 -Quiet     # no text; returns an object (used by usb-read-test.ps1)
#
# How to read it:
#   - The root-port name in the location path: ACPI(HSxx) = the port's USB 2 lane,
#     ACPI(SSxx) = its USB 3 lane. Everything behind a hub runs on the hub's lane.
#   - A USB 3 hub appears twice: a USB 2 half (on an HS lane) and a USB 3 half (on an
#     SS lane, usually named "... SuperSpeed ..." or "USB3.x Hub"). If only the USB 2
#     half is present, the hub's USB 3 link to the PC never came up.
#   - Storage driver: UASPStor = UAS protocol, USBSTOR = the older Bulk-Only protocol.
#     This does NOT show the speed: the XSTAR bridge used UAS at USB 2 too (2026-09-30).
#   - The lane shows what was negotiated; only usb-read-test.ps1 shows real throughput.
param([string]$Drive = 'D:', [switch]$Quiet)
$ErrorActionPreference = 'Stop'
$Drive = $Drive.TrimEnd('\').ToUpper()

function Prop($id, $key) {
    try { (Get-PnpDeviceProperty -InstanceId $id -KeyName $key -ErrorAction Stop).Data } catch { $null }
}

function Lane($id) {
    foreach ($p in @(Prop $id 'DEVPKEY_Device_LocationPaths')) {
        if ($p -match 'ACPI\((HS|SS)(\d+)\)$') { return $Matches[1] + $Matches[2] }
    }
    return $null
}

# ---- the drive -> its disk -> the Plug and Play chain ----
$ld = Get-CimInstance Win32_LogicalDisk -Filter "DeviceID='$Drive'"
if (-not $ld) { throw "No drive $Drive is mounted." }
$part = Get-CimAssociatedInstance -InputObject $ld -ResultClassName Win32_DiskPartition
$disk = Get-CimAssociatedInstance -InputObject $part -ResultClassName Win32_DiskDrive | Select-Object -First 1

$chain = @()
$id = $disk.PNPDeviceID
while ($id) {
    $chain += [pscustomobject]@{
        Id = $id; Name = Prop $id 'DEVPKEY_NAME'; Service = Prop $id 'DEVPKEY_Device_Service'; Lane = Lane $id
    }
    if ($id -like 'PCI\*') { break }
    $id = Prop $id 'DEVPKEY_Device_Parent'
}
$storage = $chain | Where-Object { $_.Service -in 'UASPStor', 'USBSTOR' } | Select-Object -First 1
$entry = $chain | Where-Object { $_.Lane } | Select-Object -First 1
$hubs = @($chain | Where-Object { $_.Service -in 'USBHUB3', 'USBHUB' -and $_.Id -notlike 'USB\ROOT_HUB*' })

$result = [pscustomobject]@{
    Drive    = $Drive
    Disk     = $disk.Index
    Model    = $disk.Model
    Lane     = if ($entry) { $entry.Lane } else { $null }
    Usb3     = if ($entry) { $entry.Lane -like 'SS*' } else { $null }
    Hubs     = ($hubs | ForEach-Object { $_.Name }) -join ', '
    Protocol = if ($storage) { $storage.Service } else { $null }
}
if ($Quiet) { return $result }

"Drive $Drive  ->  disk $($disk.Index): $($disk.Model)  ($([math]::Round($disk.Size / 1GB)) GB)"
""
"Device chain, from the disk up to the USB controller:"
foreach ($c in $chain) {
    "  {0,-44} driver {1,-9} {2,-10} {3}" -f $c.Name, $c.Service, $(if ($c.Lane) { "lane " + $c.Lane } else { "" }), $c.Id
}
""
if ($storage) { "Storage protocol : " + $(if ($storage.Service -eq 'UASPStor') { "UAS (UASPStor)" } else { "Bulk-Only (USBSTOR)" }) + "  (not proof of speed; see the lane)" }
if ($hubs.Count) { "Through hub(s)   : " + $result.Hubs }
if ($entry) {
    $kind = if ($result.Usb3) { "USB 3 lane (SuperSpeed)" } else { "USB 2 lane (High Speed, 480 Mbps max)" }
    "Laptop port lane : $($entry.Lane) = $kind, entered via: $($entry.Name)"
} else {
    "Laptop port lane : not reported by the firmware for this chain"
}

# ---- every external hub Windows sees right now, and which lane it's on ----
""
"USB hubs present (a USB 3 hub should show on an HS lane AND an SS lane):"
$found = $false
foreach ($h in Get-CimInstance Win32_PnPEntity -Filter "Service='USBHUB3' OR Service='USBHUB'") {
    if ($h.PNPDeviceID -like 'USB\ROOT_HUB*') { continue }
    $found = $true
    $lane = Lane $h.PNPDeviceID
    "  {0,-28} {1,-6} {2}   (parent {3})" -f $h.Name, $(if ($lane) { $lane } else { "-" }), $h.PNPDeviceID, (Prop $h.PNPDeviceID 'DEVPKEY_Device_Parent')
}
if (-not $found) { "  (none)" }
