# Read before you change anything

Most of what this app relies on was learned by measuring the owner's laptop,
not from documentation. The expensive mistakes so far all came from guessing:
the wrong IOCTL code, power-cycling ports that share power, and not knowing
that Task Scheduler ends its programs at sign-out. So you read before you write.

## At the start of every session

1. Read `CLAUDE.md` and every file in `.claude/rules/`.
2. Read `docs/RESULTS.md` and the topic files it links in `docs/results/`
   (Incidents lives in `docs/results/incidents.md`). At minimum, read **Open
   items**, **Incidents** and the section for the area you'll work on.
3. If you were handed a plan, read all of it before you start its first step.

## Before changing code

- Read the whole file you're about to change, not just the method. Grep for
  every caller of anything whose behaviour you change.
- Read what the table lists for the area you're touching. If a change spans
  several areas, read all of them.

| Touching | Read first |
|---|---|
| Shutdown: `MainForm.WndProc`, `TrayApp.OnShutdown`, `DriveEjector`, `DriveUsers` | CLAUDE.md safety rules 4–7; RESULTS **Shutdown and startup**, **Eject and device state**, **Open items** |
| Reconnect, port power | Safety rules 2–3; RESULTS **Incidents** (the flash drive), **USB hardware and IOCTLs** |
| Eject, or anything else that changes a device's state | Safety rule 2; RESULTS **Eject and device state** |
| Start with Windows: `Settings.SetStartup`, `StartupTask`, `Installer` | Safety rules 5 and 8; RESULTS **Shutdown and startup** |
| Scanning: `UsbScanner`, `Native`, `DeviceDetails` | Safety rule 1; RESULTS **USB hardware and IOCTLs** |
| Device-change handling, alerts | RESULTS **Open items** (missed quick reconnects); `performance.md` |
| Drawing and layout | CLAUDE.md code conventions; RESULTS **WinForms drawing** |
| Build, scripts, tests | CLAUDE.md "Build and release"; `testing.md`; RESULTS **Tooling** |
| Anything that runs while the app sits in the tray | `performance.md` |

## Official documentation

- Read the API's Microsoft Learn page before you use a Windows or .NET API,
  window message, IOCTL, registry value or COM object for the first time, or
  change how an existing call is made. Check:
  - its parameters
  - its return and error codes
  - whether it needs admin rights
  - whether it can block, and for how long
  - which Windows version it needs

  For managed APIs, read the **.NET Framework 4.x** page. Some newer APIs don't
  exist there, and the compiler only accepts C# 5.
- Take numeric constants (IOCTL codes, message ids, flags, GUIDs, struct
  offsets) from the documentation or the SDK header, never from memory. Next to
  each one, add a comment naming its source. Example: the wrong
  `IOCTL_USB_GET_PORT_CONNECTOR_PROPERTIES` code (0x220468 instead of 0x220458)
  cost a whole debugging round.
- When the documentation and measured behaviour disagree, the measured
  behaviour wins. Record the difference in `docs/RESULTS.md`.
- Write anything non-obvious you learned into `docs/RESULTS.md`, so the next
  session doesn't have to look it up again.

## When you can't confirm something

If you can't find the documentation, or can't confirm a behaviour safely, say
so and ask the owner. Don't guess. This matters most for anything that
disconnects, ejects, powers or writes to hardware.
