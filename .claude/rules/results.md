# Results log: write down what you learn

`docs/RESULTS.md` is the project's memory. It holds:

- what was measured
- what was tried
- what went wrong
- how Windows and this hardware actually behave
- the decisions made along the way, with their reasons

The next session starts by reading it (`read-first.md`), so anything left out
is lost.

## When to write an entry

- You measured something: RAM, CPU, a speed, a timing, or a count from the
  event log.
- A test, especially an integration or hardware test, showed something new, or
  failed in an informative way.
- Something went wrong: an incident, an error in `app.log`, a device that
  misbehaved.
- An API or Windows behaved differently from its documentation or from what
  was expected.
- You decided **not** to do something. Record why, so it isn't proposed again
  without new information.
- A release: the version, what changed, and the numbers from `.\test.ps1 -Perf`.

## Format

Put each entry under the right topic heading, newest first:

    ### 2026-09-29: Short title
    - **What:** the question or the event.
    - **How:** command, test, device and port. Enough detail to repeat it.
    - **Result:** numbers and facts only.
    - **So:** what we do because of it (a rule, a fix, an open question).

- Use absolute dates (YYYY-MM-DD, local time), never "yesterday".
- Keep measured facts apart from guesses. Label a guess **Suspected:** until
  something confirms or rules it out.
- Only append; don't rewrite history. When a later result contradicts an
  entry, add a new entry and mark the old one `Superseded by <date: title>`.
- Keep **Open items** current. When one is resolved, remove it there and write
  the outcome as an entry.
- The repository is public on GitHub. Record hardware facts only: no personal
  files, no file names from the owner's drives, no account names and no serial
  numbers.
- Keep it under 500 lines (`file-size.md`).
