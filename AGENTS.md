# AGENTS.md — guide for AI coding agents (and new contributors)

A WinForms disk cleaner / system tune-up tool for Windows: junk files, registry
residue, startup entries, installed programs, large files and duplicates. One
~235 KB portable exe, **zero dependencies, zero toolchains**: it builds with the
`csc.exe` compiler that ships inside Windows (.NET Framework 4.8). Keep it that
way. Licensed under Apache 2.0 (`LICENSE`).

## Build & test

```powershell
.\build.ps1   # builds WindowsStalker.exe with C:\Windows\Microsoft.NET\...\csc.exe
.\test.ps1    # compiles src\ + tests\ into WindowsStalker.Tests.exe and runs it
```

Run **both** after every change. There is no .sln/.csproj and there must not be
one — both scripts glob `src\*.cs` (+ `tests\*.cs`); a new framework reference
means editing the `/r:` lists in **both** scripts. CI
(`.github/workflows/tests.yml`) runs exactly these two scripts on every PR.

`build.ps1` compiles twice on purpose: pass 1 produces an exe that can render
`app.ico` (`--write-icon`, see `src/Branding.cs`), pass 2 embeds it via
`/win32icon`. Do not "optimize" this into one pass by committing an `.ico` —
the point is that the repository holds no binary assets and the taskbar icon
can never drift from the mark drawn in the UI. Note that the icon step uses
`Start-Process -Wait`: PowerShell does not wait for a Windows-subsystem exe, so
`& $exe` would return before the file exists.

## Hard constraints

- **C# 5 only** — the built-in compiler (v4.0.30319) rejects anything newer.
  No `$"..."` interpolation, no `?.`/`??=`, no `nameof`, no expression-bodied
  members, no `out var`, no pattern matching, no tuples, no auto-property
  initializers. Anonymous callbacks use `delegate(...) { }` syntax.
- **.NET Framework 4.8 BCL only** — no NuGet, no third-party libraries, no image
  assets (every glyph is GDI+ vector drawing in `src/Icons.cs`/`src/Branding.cs`).
- **UTF-8 sources** (`/codepage:65001`); Ukrainian literals are normal.
- The app runs **non-elevated**. Install/uninstall are per-user
  (`%LocalAppData%\Programs\WindowsStalker`, HKCU, per-user shortcuts). The only
  elevated code path is the short-lived `--run-job` helper (`src/ElevatedJob.cs`).
- Never commit build outputs, `app.ico`, `settings.ini`, `clean.log` or
  `backups/` (all gitignored).

## Architecture

One `MainForm` class split into partial files by concern, plus a small set of
plain-data types the tests can reach without a window.

| File | Concern |
|------|---------|
| `src/MainForm.cs` | app-lifetime state fields, `Main()`, single-instance handshake, tray icon, activity log, autostart |
| `src/MainForm.Ui.cs` | all UI construction, the seven pages, `ApplyLanguage()`, the shared busy/progress plumbing |
| `src/MainForm.Cleaner.cs` | the rule list, the background analysis, the clean itself, the auto-clean schedule |
| `src/MainForm.Registry.cs` | the six registry scanners and the backed-up fix |
| `src/MainForm.Startup.cs` | startup entries and the StartupApproved enable/disable |
| `src/MainForm.Apps.cs` | the installed-programs inventory and uninstaller hand-off |
| `src/MainForm.Space.cs` | large-file and duplicate finders |
| `src/MainForm.Settings.cs` | `settings.ini` load/save, paths, install detection |
| `src/MainForm.Install.cs` | per-user install/uninstall, shortcuts, the Apps-list entry |
| `src/MainForm.Updates.cs` | the GitHub-Releases self-updater, and the project/author URLs the About dialog links to |
| `src/MainForm.About.cs` | the About dialog (mark, version, quick start, project links) |
| `src/CleanRules.cs` | the junk catalog — **data, not code paths** |
| `src/CleanScan.cs` | per-analysis state (`CleanScan`, `RuleResult`) |
| `src/ElevatedJob.cs` | the job-file format and the `--run-job` elevated helper |
| `src/RegBackup.cs` | `.reg` writing (the escaping regedit actually accepts) |
| `src/Util.cs` | sizes, the path guard, deletion, command-line parsing, hashing, shell interop |
| `src/Controls.cs`, `src/Icons.cs`, `src/Theme.cs`, `src/Branding.cs` | custom-drawn controls, glyphs, palette, the app mark and ICO writer |
| `src/Lang.cs` | the English/Ukrainian string table |

UI is built in code; the window is fixed-size (`FixedSingle`, `ClientSize`
1024×706) and every page is a hand-tuned absolute layout against
`PageW`×`PageH` (1024×591). All state lives on the UI thread — background work
goes through `ThreadPool` and marshals back via `OnUi(...)`, which swallows the
form-already-closed race.

Per-analysis state lives in a `CleanScan` object (`MainForm.scan`), replaced
wholesale by `NewSession()`. A background worker captures the session it was
started for and compares `scan != session` before touching the UI, so a
superseded analysis writes into its own dead object and nothing leaks between
runs.

Every page is assembled from the same three pieces (the shape the AV project
uses): a `StatStrip` of numbers across the top, a `CardPanel` filling the rest
with the list inside it, and a row of buttons along the bottom of that card.
`BuildListPage`/`CardList`/`ButtonRowY` in `MainForm.Ui.cs` hold that geometry
in named constants, so the five list pages line up pixel for pixel — add a page
through those helpers rather than with fresh coordinates.

### The WinForms traps this layout already hit

Each cost a debugging round; do not reintroduce them.

- **Dock order is reverse z-order.** Docked children are laid out from the
  *highest* index down, each taking a bite out of what is left. The `Fill`
  control must therefore sit at index 0 (`Controls.SetChildIndex(pageHost, 0)`)
  — at any other index it swallows the entire client area and the header ends
  up painted on top of the pages.
- **Header children paint in z-order too.** The nav tabs are positioned by
  `LayoutNavTabs()` rather than by a `FlowLayoutPanel`: an auto-sizing docked
  panel measures itself before `ApplyLanguage()` has given the tabs their text,
  and clips the first one. For the same reason the wordmark label is sized to
  the wordmark — an over-wide label added earlier silently covers a tab.
- **An owner-drawn `DrawItem` must not fill the row.** Moving the mouse across a
  `ListView` makes Windows invalidate only the hot row, and only `DrawItem` is
  raised for it — a background fill there wipes every sub-item that
  `DrawSubItem` is not asked to repaint, and the list loses all its columns but
  the first the moment the pointer touches it. `DarkList.OnDrawItem` is
  deliberately empty; each cell paints its own background in `OnDrawSubItem`.
  The selection is a bar down the left edge of the first cell rather than a
  rectangle, which would leave a seam between every pair of cells.
- **`Theme.Card` is invisible on a card.** A secondary button filled with the
  card colour reads as a plain label. Buttons that sit on a `CardPanel` use
  `Theme.Subtle`; only tiles on the page background use `Theme.Card`.
- **Nothing hard-codes a colour or a font.** The look is the amber CRT of
  `WastelandNext` (`src/renderer/styles.css` `:root`), ported into `src/Theme.cs`
  so both apps read as the same machine: near-black glass, phosphor amber,
  dashed rules, square corners (`Theme.Radius` is 0) and scanlines. Type goes
  through `Theme.Ui`/`Theme.UiBold` (and the `Px` variants), which resolve the
  monospace stack once and apply the one `FontScale` that keeps a fixed
  1024x706 of hand-placed controls from overflowing — never `new Font("...")` at
  a call site. Surfaces go through `Theme.Surface`/`Theme.PaintCard`, which carry
  the scanlines; a plain `Panel` paints a clean slab and breaks them mid-window,
  which is why pages are `CrtPanel`. A label that sits on a painted surface must
  be `Color.Transparent`, or it punches an unlined hole in it — that is what hid
  the banner watermark.
- **A control added later sits *under* one added earlier.** The Stop buttons
  that share a cell with the action they replace (`dashStop` over `dashAnalyze`,
  `btnSpaceStop` over the two Space scan buttons) were added after it, so simply
  setting `Visible = true` put them behind an opaque tile and the dashboard Stop
  never appeared at all. `SwapStop` in `MainForm.Ui.cs` hides what the Stop
  covers and calls `BringToFront()`; use it rather than toggling `Visible`.
- **`OnTextChanged` does not repaint a `UserPaint` control.** Only the
  `ResizeRedraw` style invalidates, and this is a monospace UI — a translated
  caption of the same length measures to the same width, so the resize never
  happens and the control keeps painting the old language. `NavTab.FitWidth` and
  `Toggle.FitWidth` both call `Invalidate()` for exactly this reason
  ("Space"/"Місце" is the pair that found it).
- **Letter-spacing is measured per string, not per character.**
  `TextRenderer.MeasureText` adds its own padding once per call, so measuring a
  run a character at a time charges that padding to every character and triples
  the tracking. `Theme.DrawTracked` steps by one monospace advance instead
  (ten glyphs measured together, divided), and `Theme.MeasureTracked` is the
  matching width — auto-sized controls like `NavTab.FitWidth` must use it, or
  the nav row overflows the header.

## Working rules

Follow the matching rule whenever a change touches one of these areas:

- **`delete-safety`** — every path that will be deleted goes through
  `Util.IsProtectedPath`, and that check lives *inside* `Util.DeleteEntry`, not
  only at its call sites. It refuses drive and share roots, the well-known shell
  folders, and any **ancestor** of one of them (which is what keeps `C:\Users`
  out of reach even though it is not itself in the list). `ElevatedJob.Validate`
  re-runs the same guard, because the job file is written by a normal-privilege
  process and read by an elevated one — treat its contents as a request, never
  as an instruction. `tests/UtilTests.cs` and `tests/ElevatedJobTests.cs` pin
  this; if you add a delete path, add the corresponding test.
- **`cleaning-rules`** — a new junk location is one entry in `Rules.Build()`
  plus its two `Lang` strings, and nothing else. A rule whose folders do not
  exist on this machine is dropped at build time, so the list never offers to
  clean software that is not installed. Anything that loses user state (cookies,
  history, recent documents) must set `Risky = true`, which keeps it out of both
  the default set and the automatic clean. A rule pointed at space that other
  programs are actively writing to needs a `MinAgeHours` floor (both temp rules
  use 24) — without it the cleaner will happily delete the folder an installer
  or a build is halfway through writing. `tests/RulesTests.cs` enforces the
  invariants over the whole catalog, so a broken entry fails the suite without
  needing a test of its own.
- **`localization`** — every user-visible string goes through `Lang.T("key")`,
  added in `src/Lang.cs` with **both** English and Ukrainian. Persistent
  controls are re-texted in `ApplyLanguage()`; a control whose entire text is a
  Lang key can just carry that key in its `Name` and `RetextByName` handles it.
  `tests/LangTests.cs` checks key parity, empty strings and `{0}` placeholder
  agreement across both languages.
- **`settings-key`** — a `settings.ini` key needs a parser line in
  `LoadSettings()` and a writer in `SaveSettings()`, and must degrade gracefully
  when missing from older files. A corrupt value must never take down startup:
  timestamps parse through `Util.TryParseTicks` (range-checked), never a bare
  `new DateTime(ticks)`. Per-rule ticks are stored as `rule.<id>=0|1`, and a
  rule the user never touched falls back to its own `DefaultOn` — which is what
  lets a new catalog entry arrive switched on for existing installs with no
  migration.
- **`registry-fixes`** — no registry entry is removed without a `.reg` backup
  written first; if `WriteBackup` returns null the fix is abandoned rather than
  degraded. `.reg` files are UTF-16 LE (regedit rejects UTF-8), and the value
  formatting in `src/RegBackup.cs` is regedit's, not ours — `hex(2)` for
  REG_EXPAND_SZ, `hex(7)` for REG_MULTI_SZ, `@` for the default value. Scanners
  are strictly read-only and report an entry only when the evidence is
  unambiguous (an Uninstall key needs *both* its install folder and its
  uninstall command to be gone).
- **`elevation`** — anything needing administrator rights is written to a job
  file and executed by `--run-job`. Do **not** spawn `powershell -Command` or a
  hidden script host to do it: a cleaner that launches hidden script children to
  delete system files is indistinguishable from malware to a heuristic scanner,
  and this app already looks unusual for writing under `%LocalAppData%`. The
  helper reports failures through its exit code and the UI must not claim
  success it did not get. The single unavoidable child shell is the detached
  `cmd /c rmdir` in uninstall — the exe cannot delete itself.
- **`cancellable-work`** — every background scan takes a `CancelFlag` and is
  reachable from a Stop button on the page that started it (`BeginBusy`/
  `EndBusy` show all of them at once, so whichever page the user is looking at
  has one). The flag has to reach as deep as the work does: checking it between
  files was not enough for the duplicate finder, because one full-file
  `SHA256.ComputeHash(stream)` on a multi-gigabyte file is a single
  uninterruptible call — `Util.HashFile` therefore reads in 64 KB blocks and
  takes the flag. A cancelled run must also discard its partial results rather
  than display them (`BuildDuplicateRows` returns early, and `StartSpaceScan`
  only fills the list when the flag is clear).
- **`self-update`** — `MainForm.Updates.cs` checks the GitHub Releases API once
  per launch and once a day after that, and swaps the exe by handing a detached
  `cmd.exe` the move-and-relaunch (a running exe cannot overwrite itself). Two
  invariants: the swap **never** interrupts work — `UpdateBusy` covers every
  running scan, the clean, and an open modal dialog (`IsWindowEnabled`) — and a
  failed check is silent unless the user pressed the button. The download is
  size-checked before it is trusted, because an error page also arrives as a
  file. Both decisions are pure and unit-tested (`AppUpdateDue`,
  `IsNewerVersion`); a tag that is not a plain dotted number never triggers a
  download. The asset name here must stay in step with what
  `.github/workflows/release.yml` uploads.
- **`testing`** — testable logic is exposed as `internal static` members and
  covered in `tests/*.cs` (zero-dependency reflection runner: every public
  static `Test*` method on a `*Tests` class runs). Prefer property tests over
  the whole catalog or the whole string table to hand-picked cases.
- **`verify`** — after a UI change, launch the built exe and look at it. Note
  that synthetic clicks posted from another process do **not** work while the
  window is occluded: WinForms only raises `Click` when `WindowFromPoint` at the
  release point returns the control's own handle. `PrintWindow(hwnd, hdc, 2)`
  captures the window without stealing focus and works even when covered, so it
  is the way to screenshot the app without disturbing whatever is in front.
