# WindowsStalker

Disk cleanup and system tune-up for Windows, in the spirit of CCleaner and
CleanMyMac — as **one ~235 KB portable executable with zero dependencies**.

No installer required, no .NET to download, no NuGet packages, no toolchain:
the whole thing is built by `csc.exe`, the C# compiler that already ships inside
Windows. Clone the repository, run `build.ps1`, and you have the app.

English and Ukrainian, an amber-CRT terminal theme, and it never asks for administrator rights
unless you tell it to clean something that genuinely needs them.

## What it does

| Page | What it is for |
|------|----------------|
| **Dashboard** | How full your drives are, how much junk the last analysis found, what has been freed so far, and one button that does the whole thing |
| **Cleaner** | ~40 known junk locations across Windows, seven browsers, common applications and developer package caches — analyzed first, deleted only after you confirm |
| **Registry** | Entries pointing at files and classes that no longer exist. Every fix writes a `.reg` backup first |
| **Startup** | What runs when you sign in, and a switch to turn it off — through the same mechanism Task Manager uses, so the two agree |
| **Apps** | Everything installed, with size and install date, and a shortcut to each program's own uninstaller |
| **Space** | The largest files in a folder you choose, and byte-for-byte duplicates. Anything deleted here goes to the Recycle Bin |
| **Settings** | Language, automatic weekly clean, autostart, per-user install, and automatic updates |

## Build

```powershell
.\build.ps1   # builds WindowsStalker.exe with C:\Windows\Microsoft.NET\...\csc.exe
.\test.ps1    # compiles src\ + tests\ into WindowsStalker.Tests.exe and runs it
```

Requires nothing but Windows itself (.NET Framework 4.8, present since Windows
10 1903). `build.ps1` runs the compiler twice on purpose: the app draws its own
icon, so the first pass produces an executable that can write `app.ico` and the
second pass embeds it. That is why the repository contains no binary assets at
all — every icon and glyph in the UI is GDI+ vector drawing.

## Safety

A program whose job is deleting files has to be conservative, so:

- **Nothing is deleted without an analysis first.** The number on the CLEAN
  button is what would actually be removed — ticked rules only, minus anything
  blocked by a running process.
- **A path guard sits under every delete.** Drive roots, the Windows and Program
  Files folders, the profile root, Documents, Desktop, Downloads and any
  *ancestor* of those are refused — by the delete routine itself, not just by
  its callers, and again inside the elevated helper. See `Util.IsProtectedPath`.
- **The temp folders keep the last 24 hours.** `%TEMP%` is live scratch space
  for every running program — a folder written to minutes ago usually belongs to
  an installer, a build or an app that is still using it, so the two temp rules
  skip anything touched within a day.
- **Risky categories are never on by default.** Cookies, browsing history and
  recent-documents lists are opt-in, and the automatic clean never touches them.
- **Registry fixes are backed up.** A `.reg` file lands in `backups\` before
  anything is removed; if the backup cannot be written, nothing is removed.
- **Large files and duplicates go to the Recycle Bin**, not into the void —
  those are your own documents, so an undo has to exist.
- **Duplicates are compared byte-for-byte**, in three passes (size, then a 64 KB
  prefix hash, then the full hash). Same size is never treated as same file.
- **The app runs non-elevated.** Cleaning `C:\Windows\Temp`, the Windows Update
  cache or an HKLM key is handed to a short-lived elevated helper that executes
  a job file and exits — no hidden script hosts, no long-running elevated UI.
- **Every long scan can be called off.** A STOP button appears on whichever page
  started the work, and the cancellation reaches inside the duplicate finder's
  file hashing, so a multi-gigabyte file cannot hold the scan open after you
  press it.

## Updates

Once per launch and once a day after that, WindowsStalker asks the GitHub
Releases API whether there is a newer tag. If there is, it downloads that
release's `WindowsStalker.exe` into `%TEMP%`, and a detached `cmd.exe` helper
waits for the app to exit, moves the new build over the old one and starts it
again — back into the tray if that is where it was.

It never interrupts anything: an analysis, a clean, a scan or an open dialog all
defer the swap to the next check. The check is one small unauthenticated API
request, it fails silently when the machine is offline, and Settings → About has
both a switch to turn it off and a button to check on demand.

## Install

WindowsStalker runs fine straight from the folder you unzipped it into. On first
start it offers to install itself for the current user instead
(`%LocalAppData%\Programs\WindowsStalker`), which adds a Start-menu entry and lets
it start with Windows. Either way no administrator rights are involved, and
Settings → Status uninstalls it again.

## Files it writes

Next to the executable: `settings.ini` (preferences and which categories you
ticked), `clean.log` (one line per action) and `backups\` (registry `.reg`
backups). Nothing else, anywhere — a pending update is staged in `%TEMP%` and
deleted again if it cannot be applied.

## License

Apache License 2.0 — see [LICENSE](LICENSE).
