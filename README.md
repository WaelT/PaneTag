# PaneTag

Persistent title bars for Windows Terminal panes. The bar stays put while you run
full-screen and interactive apps such as `claude`, `codex` or `edit`, so you can always
tell which pane is which.

```
┌─────────────────────────────────────────────────────┐
│ Backend API                            ✳ Claude Code │  <- the bar: your title, the app's title on the right
│ > fix the failing auth test                         │
│ ...                                                 │
└─────────────────────────────────────────────────────┘
```

Windows Terminal has no per-pane titles of its own, and apps overwrite the tab title.
PaneTag reserves one row of the pane for a bar that apps can't draw over.

## Quick start

```powershell
git clone https://github.com/<you>/PaneTag.git
cd PaneTag
.\install.ps1              # build the helper and install the module (PowerShell 5.1 and 7)
```

Then, in any pane:

```powershell
ptag 'Backend API'         # add a bar (starts a fresh shell under it)
claude                     # the bar stays
ptag 'Backend (debug)'     # rename from inside the pane
exit                       # remove the bar
```

No .NET SDK is needed: the helper is compiled with the C# compiler that ships with Windows.

## Top or bottom?

The bar goes on **top** by default. Windows Terminal only keeps lines that scroll off screen
when the scrolling area starts at the top row, so with a top bar **you lose scrollback**.
If you scroll back through long Claude Code sessions, use a bottom bar:

```powershell
ptag 'Backend API' -Position Bottom
$env:PANETAG_POSITION = 'bottom'   # in your $PROFILE, to make it the default
```

## Documentation

| Document | What's in it |
| --- | --- |
| [docs/usage.md](docs/usage.md) | Full command reference, colors, Windows Terminal profiles, key bindings and layout scripts |
| [docs/architecture.md](docs/architecture.md) | How it works: pseudo console, escape-sequence rewriting, threads, pipe protocol |
| [docs/troubleshooting.md](docs/troubleshooting.md) | Common problems and fixes |
| [docs/development.md](docs/development.md) | Building, testing and changing the code |
| [CHANGELOG.md](CHANGELOG.md) | Release history |
| [AGENTS.md](AGENTS.md) | Instructions for coding agents working on this repo |

## Requirements

- Windows 10 1809 or later (Windows 11 recommended)
- Windows Terminal (other terminals that support standard escape sequences should work, untested)
- Windows PowerShell 5.1 or PowerShell 7
- .NET Framework 4.x (built into Windows)

## Limitations

- Apps get one row less than the pane.
- With a top bar, lines that scroll off screen are not kept in scrollback (see above).
- Mouse clicks on the bar are not passed to mouse-aware apps.
- Emoji and East Asian characters in titles are measured approximately; the bar is still painted full width.

## License

[MIT](LICENSE)
