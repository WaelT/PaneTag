# Changelog

All notable changes to this project are documented here. Versions follow
[Semantic Versioning](https://semver.org/).

## [Unreleased]

### Added

- Status timer: `ptag --status ... --timer` (`-Timer` in PowerShell) shows how long the
  status has been up once a minute has passed (`● Needs you · 4m`, `· 1h 05m`), so with
  several flagged panes you can tell which has waited longest. The Claude Code hooks use
  it for "Your turn" and "Needs you"; run `Enable-PaneTagClaudeHooks` again to get it.

### Fixed

- The smoke tests for out-of-order status updates (0.3.1) built their pipe lines wrongly
  and passed without testing anything. They now check the behavior, with positive controls.

## [0.3.1] - 2026-10-17

### Changed

- New README demo (`docs/assets/panetag-demo.gif`) showing status flags and the Claude
  Code hooks; `tools/demo/play.ps1` plays the scene for recording by hand.
- `tools/publish.ps1` publishes with PSResourceGet instead of PowerShellGet, which fails
  with "dotnet.exe failed to pack" on machines with the .NET runtime but no SDK.
- Docs: installing from the PowerShell Gallery, badges, and Windows Terminal examples that
  look up the (versioned) module folder.
- Docs: `docs/alternatives.md` (similar tools on Windows and macOS) and `CONTRIBUTING.md`
  (how to report bugs and contribute; contributions are MIT-licensed, no ownership transfer).

### Fixed

- "Your turn" stayed up while Claude was working again without a prompt (a background
  task or agent finished, `/loop`, a scheduled run) until you pressed a key.
  `Enable-PaneTagClaudeHooks` now also adds a `PreToolUse` hook that clears the status;
  run it again to get it.
- Async hooks fired close together (e.g. `Stop`, then a queued prompt) could reach the bar
  out of order and leave a stale status. `ptag.exe` now stamps status updates with its
  process creation time and the bar drops older ones, plus non-sticky statuses raised
  before your last keypress.
- `tests/smoke.ps1` restores `PANETAG_PIPE` when it finishes, so running the tests inside
  your own titled pane no longer breaks `ptag` there.

## [0.3.0] - 2026-10-16

### Added

- Status flags: `ptag --status Your turn` recolors the bar (orange by default) and adds a
  label until your next keypress in the pane. `--status-color`, `--sticky` and
  `--clear-status` control it; PowerShell has `-Status`, `-StatusColor`, `-Sticky`,
  `-ClearStatus`. Outside a titled pane status updates are a silent no-op.
- `Enable-PaneTagClaudeHooks` / `Disable-PaneTagClaudeHooks`: Claude Code hooks that turn
  the bar orange ("Your turn") when Claude finishes and red ("Needs you") when it asks for
  permission. Existing settings are kept and backed up.
- `ptag Reviewing PR` without quotes now works in PowerShell too.
- Smoke tests run on GitHub Actions.
- Published on the PowerShell Gallery: `Install-Module PaneTag`.

## [0.2.0] - 2026-10-16

### Added

- `ptag` now works in every shell inside a titled pane, not just PowerShell: the build
  adds a `ptag.exe` copy and each titled pane puts it on its PATH. Rename while an app is
  running with Claude Code's `!` commands: `! ptag Reviewing PR`. It's instant.
- Demo GIF in the README.

### Changed

- `panetag.exe` takes the title as plain words (`panetag Backend API -c blue`); quotes are
  optional. **The command to run must now follow `--`** (`panetag Agent -- claude`).
- Inside a pane that already has a bar, `panetag`/`ptag` change that bar instead of
  starting a second one. Start-only options (`-p`, `--cwd`, a command) are refused there.

## [0.1.0] - 2026-10-15

First release.

### Added

- `panetag.exe`: runs a shell or command in a pseudo console one row shorter than the
  pane and keeps a title bar on the reserved row, including under full-screen apps.
- Top (default) or bottom bar position. A bottom bar keeps Windows Terminal scrollback.
- Bar shows your title on the left and the running app's window title on the right.
  The tab title becomes `Your title · app title`, and the original is restored on exit.
- Named colors, `#RRGGBB` colors, automatic text contrast, and a stable automatic color per title.
- Rename and recolor a running bar over a named pipe (`PANETAG_PIPE`).
- Resizes follow the pane; mouse and cursor-position reports are adjusted for a top bar.
- `PaneTag` PowerShell module with `Set-PaneTag` (alias `ptag`), for Windows
  PowerShell 5.1 and PowerShell 7.
- `build.ps1` (no SDK needed), `install.ps1`, and headless smoke tests in `tests/`.
