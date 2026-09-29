# Incidents

Things that went wrong, or tests that told us something new about the hardware.
Newest first. Part of the results log: see `../RESULTS.md` and
`.claude/rules/results.md`.

### 2026-09-30: D: file system checked after the replug test: no problems
- **What:** An online check of D:. The replug test had pulled D: out while it
  was mounted, and NTFS had logged event 140 during earlier drops.
- **How:** `chkdsk D: /scan`, run elevated after the owner approved the UAC
  prompt. The report is in `dist\chkdsk-D.txt`.
- **Result:**
  - "Windows has scanned the file system and found no problems."
  - 41,728 file records, 42,598 index entries and 33,738 files checked.
  - 0 KB in bad sectors, 225 GB free.
  - The scan took 1.5 s.
- **So:** No repair (`/f`) is needed. The game-file verify is still worth doing
  in the launcher, because chkdsk checks the file system, not what's inside the
  files.

### 2026-09-30 00:29: Replugging the hub repeatedly left the SSD stuck at Code 10
- **What:** For a minute, the owner pulled the hub out of the laptop and put
  it back, over and over, mostly on Port 1 and once on Port 2. The goal was to
  see whether the app falls back to showing USB 2 while the real speed stays at
  USB 3.
- **How:**
  - `tools\usb-read-test.ps1 -Seconds 60`.
  - A monitor that recorded, every second, what USB Ports' scanner reported
    and the SSD's parent in Windows' device tree.
- **Result:**
  - The first pull, at 00:29:27, removed D: while it was mounted. D: never came
    back during the minute, so no speed could be measured.
  - Kernel-PnP logged about 14 hub disconnects.
  - Whenever Windows listed the SSD, it was behind the hub's **USB 3 half**,
    never the USB 2 half.
  - The SSD's USB device ended in **problem code 10 (failed to start)**:
    present, but with no disk. That's why the app didn't list it.
- **Suspected:** The hub has its own power supply, so pulling it from the
  laptop probably didn't cut the SSD's power. The SSD's USB adapter was reset
  many times without a power cycle and got stuck. This matches the earlier
  "connected but not usable" states.
- **Afterwards (00:31):**
  - The owner plugged the hub in again, with the SSD attached. Windows beeped
    connect and disconnect at a fixed rhythm.
  - Kernel-PnP logged the hub's SuperSpeed half dropping every **~3 s**: on
    Port 2 (&0&19) at 00:31:10–16, and on Port 1 (&0&17) at 00:31:25, :27, :30,
    :33, :36, :39 and :42.
  - A fixed period means a retry loop in the hub or the SSD's adapter, not a
    loose contact, which would be random.
- **So:**
  - The hub and the SSD both have to lose power to reset: unplug the hub's
    power supply and the SSD for about 15 s. Pulling the hub from the laptop
    isn't enough, because it has its own power.
  - This is the pattern the planned "keeps reconnecting" alert (plan, phase 1)
    should catch.
- **Recovered (00:34):**
  - After the full power-off, D: mounted at USB 3 (SS01, through the hub's
    USB 3 half), and NTFS 98 reported it healthy.
  - A 10 s read test ran at 383 MB/s median with no errors.
  - This confirms the power-off reset fixes the stuck loop.
  - To repeat this test, wait until D: is back after each replug before
    pulling again. Every pull while D: is mounted is an unsafe removal.

### 2026-09-30: 1-minute test, hub held still, only the SSD cable wiggled
- **What:** The owner held the hub still and wiggled only the SSD cable at
  the hub, the test proposed in the next entry.
- **How:** `tools\usb-read-test.ps1 -Seconds 60`.
- **Result:**
  - Nothing dropped: no disconnects, errors, retries or resets.
  - Seconds 2 to 20 ran at a steady **~255 MB/s**. From second 21 the speed
    jumped straight to **~380 MB/s** and stayed there.
  - Second 1 had one 530 ms read.
  - Earlier runs started at 345 to 370 MB/s on the same first file.
- **Suspected:**
  - The slow first 20 s were a degraded link while the cable was moving. USB 3
    fixes errors on the cable by resending data, which Windows doesn't log,
    and that costs speed without a disconnect. Not confirmed; it depends on
    when the wiggling stopped.
  - One clean minute leans toward explanation 1 in the next entry (the hub's
    plug moving in the laptop). The earlier drop took about 2 minutes to
    appear, so a minute proves little.

### 2026-09-30 00:08: 10-minute soak through the hub on Port 1: the USB 3 lane dropped once
- **What:** A 10-minute read soak on D: behind the self-powered hub on Port 1,
  at USB 3. The owner was asked to wiggle the cables during it.
- **How:**
  - `tools\usb-read-test.ps1 -Seconds 600`, read-only.
  - Kernel-PnP/Device Management log, and each device's
    `DEVPKEY_Device_LastArrivalDate`.
- **Result:**
  - The median speed was 370 MB/s. There was one drop, at 00:10:38, about
    1 min 50 s into the run:
    - First came two disk 153 retries.
    - Then Kernel-PnP 1010 reported "USB\VID_05E3&PID_0626 (the hub's
      **SuperSpeed half**) surprise removed, missing on the bus".
    - D: was removed at 00:10:42, and NTFS 140 said it had failed to flush
      its log.
    - At 00:10:43 the hub's SuperSpeed half and the SSD came back. NTFS 98
      said D: was healthy.
  - The hub's **USB 2 half stayed connected**: its last arrival is still
    00:06:13.
  - After the drop there were 490 s with no problems.
- **So:**
  - The link that failed was the USB 3 data lane between the laptop's Port 1
    and the hub. The hub's power and the USB 2 lane of the same plug kept
    working, so a **power shortage is ruled out** for this drop.
  - The owner was wiggling **the SSD's cable at the hub**, not the hub's plug
    at the laptop. Yet the device that dropped was the hub's link to the PC,
    not the SSD behind the hub. Two possible explanations:
    - The movement shifted the hub and its plug in Port 1.
    - The SSD cable's loose contact disturbed the downstream port and reset
      the hub's USB 3 chip.
  - Next test: hold the hub and its laptop plug still, and wiggle only the SSD
    cable. `usb-read-test.ps1` now names the device that dropped (Kernel-PnP
    1010), which tells the two explanations apart.
  - It was 1 drop in 10 minutes of wiggling, against bursts before. That's
    better, but not solved.
  - Run `chkdsk D: /scan` (admin, online), because NTFS 140 appeared again.

### 2026-09-30: The hub runs at USB 3 on Port 1
- **What:** After the SSD was ejected by the test, the owner replugged it. It is
  behind the same Genesys hub on Port 1.
- **How:**
  - `tools\usb-link-check.ps1`, which reads Windows' device tree.
  - `tools\usb-read-test.ps1 -Seconds 30`: unbuffered reads of 38 existing
    files, about 132 GB in total. Nothing was written.
- **Result:**
  - Both halves of the hub are present: the SuperSpeed hub 05E3:0626 on SS01
    and the USB 2 hub 05E3:0610 on HS01. The SSD is behind the SuperSpeed half.
  - Read speed: median 370 MB/s, lowest second 335 MB/s, slowest single 1 MiB
    read 37 ms.
  - No disconnects, read errors, stalls, retries or resets during the test.
- **So:**
  - Port 1 and the hub both work at USB 3. The hub's earlier USB 2-only links
    (Port 2 at 23:52, Port 1 at 23:59) happened at plug-in and aren't
    permanent.
  - **Suspected:** a slow or partial insertion lets the USB 2 pins, which make
    contact first, win, so the link stays at USB 2 until the next replug.
  - A 10-minute soak test with the cable wiggled comes next.

### 2026-09-30: Claude's tests ejected the SSD twice (Windows redirected the "eject off" setting)
- **What:** During plan step 0.6, two things ejected D: (XSTAR SS D 512GB). The
  first was a simulated shutdown sent to the owner's running copy, at 23:56:48
  on 2026-09-29. The second was an experiment copy started through
  `explorer.exe`, at 00:02:57. The second is the unexplained eject at 00:02:56
  in the next entry. Both were normal safe ejects (`CM_Request_Device_Eject`),
  not surprise removals. The owner replugged the SSD both times.
- **How:** The PowerShell tool of the Claude desktop app set
  `HKCU\Software\UsbPorts\EjectOnShutdown = 0` and read it back as 0, then sent
  WM_QUERYENDSESSION / WM_ENDSESSION. The experiment copy's own key
  (`Software\UsbPorts.Experiment`) was set the same way.
- **Result:**
  - The Claude desktop app is an MSIX package (`Claude_pzs8sxrjxfjjc`), and
    every process its tools start runs inside that package (see Tooling).
  - Windows redirected the registry write into the package's private registry,
    so the read-back said "0". The app runs outside the package and saw no
    value at all, which means "on", so it ejected.
- **So:**
  - Never set a USB Ports setting from Claude's tools to make a test safe; the
    app won't see it. Turn it off in the app's own Settings page, or don't run
    the test.
  - Never send session-end messages to a copy of USB Ports while a drive is
    attached.
  - The simulated-shutdown steps in CLAUDE.md need changing (owner to decide).

### 2026-09-30: The hub's USB 3 half is missing on Port 1 too
- **What:** The owner moved the hub, with the SSD behind it, to Port 1.
- **How:** `tools\usb-link-check.ps1`. It reads Windows' own device tree and
  location paths, not USB Ports' scanner.
- **Result:**
  - The hub connected on lane HS01 (USB 2). No SuperSpeed hub appeared, just as
    on Port 2.
  - The SSD's bridge used UAS (`UASPStor`) even at USB 2, so the storage driver
    is no evidence of the speed.
- **So:**
  - The hub's USB 3 link didn't come up on either port. The hub or its cable is
    now the more likely cause, not Port 2's contacts; this replaces the
    "Suspected" line in the next entry.
  - The owner says the ports and the SSD work at USB 3. Next, connect the SSD
    directly, then run `tools\usb-read-test.ps1` for the speed and a long run
    with the cable wiggled for stability.
  - Before the speed test could run, the SSD was ejected at 00:02:56 and was
    left waiting to be unplugged (problem code 47). A Claude test caused it
    (see "Claude's tests ejected the SSD twice" above).
  - **Superseded by "2026-09-30: The hub runs at USB 3 on Port 1".** After a
    replug the hub did link at USB 3, so the hub is not limited to USB 2.

### 2026-09-29 23:52: SSD through a powered hub on Port 2: USB 2, no drops yet
- **What:** The owner plugged the SSD into a hub and the hub into Port 2. The
  SSD runs at USB 2, and the owner has seen no disconnects.
- **How:**
  - A read-only scan with the app's own `UsbScanner` and `DeviceDetails`.
  - The Partition/Diagnostic 1006 and Ntfs events since 23:30.
- **Result:**
  - The hub is a Genesys Logic 05E3:0610 "USB2.1 Hub": USB version 2.10, 4
    ports, "has its own power supply".
  - The hub reports that it can run at USB 3 (EX_V2 flag 2), so it is a USB 3
    hub. Only its USB 2 half connected, on HS03. Nothing appeared on Port 2's
    USB 3 half (connection 19).
  - The SSD runs at USB 2 behind the hub and asks for 500 mA.
  - On both mounts (23:51:22 and 23:52:32) NTFS reported D: healthy (event 98).
    No drops between 23:52:32 and 23:55:47; that's too short to judge.
- **Suspected:** Port 2's USB 3 contacts don't connect. The SSD's earlier drops
  all happened at USB 3 on this port.
- **So:**
  - Next, test the same hub on Port 1, which is free. If it connects at USB 3
    there, Port 2's USB 3 pins are at fault.
  - The USB 2 advice (plan, phase 3) needs a case for a USB 3-capable hub that
    is running at USB 2.

### 2026-09-29: The SSD keeps disconnecting on the laptop's ports
- **What:** The SSD cable drops and reconnects at the slightest touch, but only
  on this laptop; other laptops run the same SSD and cable fine. The owner
  hadn't noticed the mouse dropping.
- **How:** The System event log for the three days up to 2026-09-29.
- **Result:**
  - 48 unexpected SSD disconnects, often in bursts (6 in about 20 s at 22:16 on
    09-29).
  - 10 mouse disconnects in the same period.
  - On D:, NTFS event 140 ×18 (the latest at 09-29 22:15), NTFS 137 ×1, disk 51
    ×203 and disk 153 ×111.
  - The SSD was on Port 2, running at USB 3.
- **Suspected:** Worn or loose contacts in the laptop's sockets. USB 3's extra
  pins are the more fragile ones. The power margin is also thin: 896 of 900 mA,
  on ports that share power.
- **So:**
  - Test Port 3 at USB 2 through a USB 2 extension or hub. Software can't force
    USB 2 (see USB hardware and IOCTLs).
  - The lasting fix is a powered USB 3 hub.
  - Avoid writing to D: until `chkdsk D: /f` has been run.

### 2026-09-29: The laptop hung after a shutdown from the sign-in screen (CMOS reset)
- **What:** The night before, the owner shut down from the sign-in screen after
  sleep. The laptop stayed half-powered: the mouse kept switching on and off,
  and the power button did nothing. The owner reset the CMOS to recover.
- **How:** The System log, `app.log` and `shutdown.log`.
- **Result:**
  - Windows logged a normal, successful shutdown.
  - USB Ports was not involved. The copy that Task Scheduler had started was
    already ended at sign-out (see Shutdown and startup).
  - After the wake, the internal Intel Bluetooth adapter was surprise-removed
    about 1 s after resume.
  - The Intel Content Protection HECI Service fails at every wake.
- **Suspected:** A firmware or embedded-controller hang after sleep.
- **So:**
  - Update the BIOS (G531GT.308 is from 02/2021) and the Intel ME driver.
  - If it happens again, unplug the charger and hold the power button for 40 s.
  - Keep app code away from sleep (see Decisions).

### Before 2026-09-27: Power-cycling the SSD's port knocked out the flash drive
- **What:** Reconnect (`IOCTL_USB_HUB_CYCLE_PORT`) on the SSD's port.
- **Result:** The SanDisk flash drive on another port dropped too, then showed
  as connected but unusable. It recovered later. On another occasion the owner
  saw the SSD drop once while the mouse's port was cycled, but that couldn't be
  reproduced.
- **So:** The ports share power. This led to safety rule 3 (the shared-power
  guard) and to the "USB ports share power" setting, which is on by default.

### Before 2026-09-27: The SSD enclosure
- **What:** In its original USB enclosure the SSD kept disconnecting and was
  slow.
- **Result:**
  - The enclosure was at fault and had corrupted data.
  - The SSD was moved to a SATA-to-USB 3 cable, wiped, and formatted as NTFS.
  - Some days it showed as connected but unusable at both USB 2 and USB 3, then
    came back later with nothing changed.
  - At USB 3 it ran a disk-heavy game well, and a game-file verify read at
    about 180 MB/s.

