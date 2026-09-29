# File size: 500 lines at most

- No file in the repo goes over **500 lines**. That covers:
  - source (`src\`)
  - tests (`tests\`)
  - scripts (`*.ps1`)
  - docs (`*.md`, including this folder and `docs\RESULTS.md`)

  Build output in `dist\` and `download\` doesn't count.
- `.\test.ps1` enforces the limit. It lists every file over 500 lines, and the
  run fails.
- Aim well below the limit. New files should stay around 300 lines. When a
  file passes about 400 lines, plan its split before adding more.
- Split along a natural seam, for example:
  - a control
  - a helper class
  - a data type
  - a `partial class` that separates a form's layout from its behaviour
  - one topic of the results log

  Don't split in the middle of a concept just to pass the check. Never squeeze
  code under the limit by joining lines or deleting comments.
- When `docs/RESULTS.md` nears the limit, move its largest topic into
  `docs/results/<topic>.md` and leave a one-line link in its place.
- An exception needs the owner's OK and a comment at the top of the file
  saying why. There are none today.
- No known violations. The last one, `src\TrayApp.cs` at 507 lines, was split
  on 2026-09-30: `Program` moved into `src\Program.cs`.
