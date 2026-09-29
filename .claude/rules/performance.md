# Performance: the app must stay light

USB Ports sits in the tray all day, so what it costs while idle matters more
than any feature. **Performance comes first in every design choice.** Only the
hardware safety rules in CLAUDE.md rank above it. Neither one is traded for a
feature.

## Budgets

These are measured on the owner's laptop: the installed copy, in the tray,
idle (window hidden, no device changes). The 1.7.1 baseline is from
2026-09-29 (see `docs/RESULTS.md`, Performance baselines).

| Measure | 1.7.1 baseline | Budget |
|---|---|---|
| Private memory (Private Bytes) | 26.6 MB | ≤ 32 MB |
| Working set (after the app trims it) | 5.6 MB | ≤ 12 MB |
| CPU while idle | no measurable use between samples | ≤ 0.05 s per minute |
| Threads | 6 | ≤ 8 |
| Handles | 351 | ≤ 450 |
| GDI objects / USER objects | 45 / 42 | ≤ 80 / ≤ 80 |
| Growth over 30 forced rescans | +0.1 MB private, −1 handle, 0 GDI, +2 USER (2026-09-30) | private ≤ +2 MB, handles ≤ +10, GDI/USER ≤ +2 |
| Main window open (Ports page) | 28.1 MB private, 11.8 MB working set (2026-09-30) | private ≤ 35.1 MB (baseline + 25 %) |

The two bottom rows were first measured by `.\test.ps1 -Perf` on 2026-09-30
(see `docs/RESULTS.md`, Performance baselines).

- A feature may add at most **2 MB** of idle private memory, and no idle CPU,
  threads or timers. If it can't, record why in RESULTS.md and ask the owner
  before going ahead.
- Never raise a budget quietly. Raising one needs the owner's OK and a
  RESULTS.md entry with the numbers before and after.
- Going over budget blocks the release. Fix it first.

## How it's measured (`.\test.ps1 -Perf`)

1. Wait until the installed app has been idle and hidden for 60 s. Then read
   its Private Bytes, working set, threads, handles, and GDI/USER objects
   (`GetGuiResources`).
2. **Idle CPU:** `TotalProcessorTime` at the start and again 60 s later.
3. **Leak check:** send `WM_DEVICECHANGE` / `DBT_DEVNODES_CHANGED` (0x219 / 7)
   to the app's hidden main window 30 times, 2 s apart. Each message triggers a
   rescan after the 1.5 s settle. Wait 10 s, measure again, and report the
   growth.
4. **Window open:** show the main window through the show signal, wait 5 s,
   measure, then hide it again.
5. Print a table (measure, value, budget, pass/fail). Any fail makes the
   script fail. Copy the numbers into RESULTS.md at every release.

## Rules for code

- **Nothing runs while idle.** No polling, timers or background threads while
  the window is hidden. React to Windows notifications instead
  (`WM_DEVICECHANGE`, session messages). Timers run only while a window is
  visible or an animation is moving.
- **Everything that can grow has a limit.** Any list, dictionary or cache kept
  between events has a maximum size and is pruned when used. Log files are
  capped (see `DriveEjector.Log`). Append a line and close the file; never keep
  it open.
- **Nothing large stays in memory while hidden.** Hidden scans use
  `UsbScanner.ReadQuick`. Free big one-off buffers, such as the handle snapshot
  in `DriveUsers`, as soon as the work ends. Call `TrimMemory` after hidden
  work, as `TrayApp` already does.
- **Create windows and views only when needed, and dispose of them on close.**
  Dispose every `Font`, `Brush`, `Pen`, `Bitmap`, `Icon` and `Graphics` you
  create. A GDI leak shows up as a growing GDI count in the leak check.
- **No heavy assemblies while idle.** Load WMI (`System.Management`),
  event-log readers and similar only on the code path that needs them. For
  example, the event-log query runs only when a session is ending.
- **Keep slow calls off the UI thread.** Ejects, handle searches and scans run
  on the thread pool, each with a time limit.
- **Measure, don't assume.** Measure any change that could affect memory or
  CPU before and after, and put the numbers in the commit message or
  RESULTS.md.
