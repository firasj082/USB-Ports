# Tests: every change ships with them

## The rule

- A feature, fix or refactor isn't done until it has tests and the whole suite
  passes. Write the tests in the same change, never "later".
- **New feature:** unit tests for its logic, plus at least one integration test
  that runs it on the real system.
- **Bug fix:** a regression test that fails without the fix and passes with it.
  Run it once before the fix to prove it catches the bug.
- **Changed behaviour:** update the tests that describe it in the same change.
  Never delete, skip or weaken a failing test to get a green run. Fix the code.
  If the test itself is wrong, say why in the commit message.
- **Refactor:** the existing tests pass without being edited.
- **Updates count too.** Every change to an existing feature follows these
  rules, not only new features.
- Before every commit, run `.\test.ps1` (unit tests and the file-size check).
- Before every release, run `.\test.ps1 -Integration`. After installing, run
  `.\test.ps1 -Perf` (see `performance.md`).
- Show the owner the summary lines (passed, failed, skipped). Never say "tests
  pass" unless you ran them in this session.

## The harness

There's no test framework and no NuGet here, so the harness lives in the repo.
If `test.ps1` or `tests\` doesn't exist yet, building them is the first job,
before any feature work.

- `test.ps1` compiles `src\*.cs` plus `tests\*.cs` into the console exe
  `dist\UsbPorts.Tests.exe` (`/target:exe /main:TestRunner`), then runs it. It
  uses the same `csc`, flags, resource and references as `build.ps1`. It exits
  non-zero if anything fails.
  - no switch: unit tests plus the file-size check (`file-size.md`)
  - `-Integration`: also runs the integration tests
  - `-Perf`: the performance check against the installed, running app
  - `-Hardware`: the hardware tests, only after the owner confirms in chat
  - `-Filter <text>`: only tests whose name contains the text
- `tests\TestRunner.cs` uses reflection to find `public static void` methods
  marked `[Unit]`, `[Integration]` or `[Hardware]`. It runs each one inside a
  try/catch and prints `PASS`, `FAIL` or `SKIP` with timings, then a summary
  line. Its exit code is the number of failures.
- `tests\Check.cs` holds the assertions: `Equal`, `True`, `False`, `Contains`
  and `Throws`. Their failure messages show both the expected and the actual
  value.
- One test file per area, named after its source file (for example
  `tests\UsbAlertsTests.cs`). Test files stay under 500 lines like every other
  file, and they're C# 5 like the app.

## Unit tests

- Pure logic only:
  - no USB IOCTLs, real devices, shown windows or network
  - no touching the owner's registry settings
  - no files outside a temp folder

  Each test takes well under 100 ms, and the whole unit run takes under 10 s.
- Design code so this is possible. Put decisions in small pure functions or
  classes that take their inputs as parameters (time, readings, flags). Keep
  Win32 and hardware calls in thin wrappers around them. Examples:
  - "is this session end a shutdown?": takes the flags, the `SM_SHUTTINGDOWN`
    value and whether event 1074 was seen
  - flap detection: takes the device and the times
  - the USB 2 advice text: takes the port, the device and the hub
- Cover the edges:
  - empty input
  - a drive with no letters
  - a port with no USB 3 half
  - a device behind a hub
  - times exactly on a window boundary

## Integration tests

- They run on real Windows with the real ports and real Win32 calls, and they
  are **read-only**. Examples:
  - scan every port twice and compare the results
  - render every form and view off-screen in both themes, save the images to
    `dist\test-output\` and look at them
  - register and unregister device notifications on a hidden window
  - feed the app synthetic window messages
  - run `DriveUsers.Find` against a test process that holds a file open on a
    `subst` drive letter
- Never change the owner's settings. `Settings` must allow a test registry key
  (e.g. `Software\UsbPorts.Test`), and the tests delete it when they finish.
  If a test truly has to change a real setting:
  - restore it in `finally`
  - check that it was restored
  - print the setting's final value

  A forgotten "Safely eject at shutdown: off" would silently break the owner's
  next shutdown.
- Never stop, restart or send messages to the owner's running copy of USB
  Ports. The only exceptions are `-Perf` and the release steps.
- **Simulated shutdowns only when no USB drive is plugged in.** This covers any
  test that sends `WM_QUERYENDSESSION` / `WM_ENDSESSION`, even to a copy inside
  the test process with a stand-in eject. Check `DriveEjector.UsbDrives()`
  right before sending; if it isn't empty, throw `SkipTest`. Details are in
  CLAUDE.md.
- Settings changed from Claude's tools don't count as a safeguard for real
  hardware. The tools run inside the Claude app's MSIX package, whose registry
  and AppData writes Windows redirects away from other apps. Only code-level
  guards count: a stand-in, or a check that refuses to run.

## Hardware tests

- Anything that ejects, power-cycles or reconnects a device, or that needs a
  device plugged in or pulled out.
- The only allowed target is the **SanDisk flash drive** (USB vendor id `0781`).
  Every hardware test finds its target itself, and refuses to run if the target
  is anything else. That includes the XSTAR SSD (D:), the mouse, any keyboard or
  input device, and any device it can't identify.
- Run them only after the owner has said in chat that the flash drive is
  plugged in. Follow safety rule 3: never power-cycle while another USB drive
  is attached, unless the owner has switched "ports share power" off.
- Record every hardware test run and its result in `docs/RESULTS.md`.
