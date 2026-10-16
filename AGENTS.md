# AGENTS.md

Instructions for coding agents (Claude Code, Codex and others) working in this repository.

## What this is

PaneTag keeps a title bar on a Windows Terminal pane that full-screen apps can't remove.
`panetag.exe` runs the shell in a pseudo console (ConPTY) one row shorter than the pane,
rewrites row-related escape sequences in the output, and draws the bar on the spare row.
A PowerShell module (`Set-PaneTag`, alias `ptag`) launches it or renames a running bar
over a named pipe.

Read [docs/architecture.md](docs/architecture.md) before changing `src/PaneTag.cs`.

## Layout

| Path | Contents |
| --- | --- |
| `src/PaneTag.cs` | All helper code: P/Invoke, `Wrapper` (pseudo console, rewriting, bar, threads, pipe), `Program` (arguments) |
| `PaneTag/` | The module as installed: `PaneTag.psd1`, `PaneTag.psm1`, and the build output `panetag.exe` plus its `ptag.exe` copy |
| `tests/PtyHarness.cs`, `tests/smoke.ps1` | Headless smoke tests |
| `docs/` | Usage, architecture, troubleshooting, development |
| `build.ps1`, `install.ps1` | Build; build and install to the user's module folders |
| `tools/publish.ps1` | Build, test and publish to the PowerShell Gallery (the user runs it) |
| `.github/workflows/smoke.yml` | Runs `tests/smoke.ps1` on GitHub Actions (Windows PowerShell 5.1) |

## Commands

```powershell
.\build.ps1                   # compile src\PaneTag.cs -> PaneTag\panetag.exe
.\tests\smoke.ps1             # build and run all smoke tests (must print "All smoke tests passed.")
.\tests\smoke.ps1 -NoBuild    # test without rebuilding
```

Don't run `install.ps1` unless the user asks: it writes to their Documents folder. Never
run `tools\publish.ps1` (it publishes to the PowerShell Gallery with the user's API key), and
never edit the user's real `~/.claude/settings.json`; test hooks with `-SettingsPath` on a
temp file.

## Hard constraints

- **C# 5 only.** The build uses the .NET Framework `csc.exe` that ships with Windows. There
  is no .NET SDK on the target machine. No string interpolation, `?.`, `nameof`,
  expression-bodied members, `out var`, tuples or pattern matching. Only .NET Framework 4.x
  APIs plus `System.Core.dll`.
- **No `\x1b` before a hex digit.** C# `\x` takes up to four hex digits, so `"\x1b7"` is
  U+01B7. Use `\u001b` (see the `SaveCursor`/`RestoreCursor` constants).
- **Module code must run on Windows PowerShell 5.1:** no ternary, `??`, `?.` or `&&`/`||`.
- **Pass strings to native programs as base64** from PowerShell 5.1 (`--title-b64`,
  `--cmd-b64`, `--cwd-b64`, `-EncodedCommand`). PS 5.1 mangles embedded double quotes.
- **No new dependencies or build tools** without asking the user.

## Invariants in `src/PaneTag.cs`

- All terminal state and every write to the pane happen while holding `gate`.
- Draw the bar only through `TryFlushHeader`. It refuses while the parser is mid-sequence
  or mid-UTF-8 character, and (unless forced) while the app has a saved cursor
  outstanding, because drawing uses `ESC 7`/`ESC 8`.
- Every row the app sends is in app coordinates (`1..InnerRows`). Convert with `Offset`
  and clamp so nothing lands on `HeaderRow`.
- Pass sequences through unchanged unless they refer to absolute rows or can wipe the bar row.
- Window-title OSCs (0 and 2) are consumed, not forwarded. The tab title is set by
  `TryFlushHeader`.

## Verifying changes

1. `.\tests\smoke.ps1` must pass. Add a scenario there for new behavior where possible.
2. The harness can't show real rendering or scrollback. For anything visual, list the
   relevant manual checks from [docs/development.md](docs/development.md#manual-checks-in-windows-terminal)
   for the user and say that they weren't run. Don't claim visual behavior you haven't seen.
3. Clean up stray `panetag` / `PtyHarness` processes left by failed tests.

## Keeping docs in sync

When behavior, options or pipe commands change, update `panetag.exe --help`
(`Program.Usage`), [docs/usage.md](docs/usage.md), the sequence table in
[docs/architecture.md](docs/architecture.md), and [CHANGELOG.md](CHANGELOG.md). Keep the
README short. Details go in `docs/`.

## Style

Match the surrounding code: 4-space indentation, braces on new lines in C#, short comments
that explain *why*, not *what*. Keep `src/PaneTag.cs` a single file unless the user wants
it split.
