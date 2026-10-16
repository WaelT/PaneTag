# Usage

There are two ways to use PaneTag:

- **The PowerShell module** (`Set-PaneTag`, alias `ptag`): the everyday way, from inside any pane.
- **`panetag.exe` directly**: for Windows Terminal profiles, key bindings and `wt` layout scripts.

Both put the same bar on the pane. The module just starts `panetag.exe` for you, or
renames the bar when you're already inside one.

## Installing

```powershell
cd PaneTag        # the cloned repository
.\install.ps1
```

This builds `PaneTag\panetag.exe` and copies the `PaneTag` module folder to:

- `Documents\WindowsPowerShell\Modules\PaneTag` (Windows PowerShell 5.1)
- `Documents\PowerShell\Modules\PaneTag` (PowerShell 7)

PowerShell loads the module automatically the first time you type `ptag` or
`Set-PaneTag`, so you don't need to edit your profile. Open a new pane after installing.

To find where it was installed (for example when Documents is redirected to OneDrive):

```powershell
(Get-Module -ListAvailable PaneTag).ModuleBase
```

To uninstall, delete those two `PaneTag` folders.

## `Set-PaneTag` / `ptag`

```
Set-PaneTag [[-Title] <string>] [-Color <string>] [-Foreground <string>]
              [-Position Top|Bottom] [-Command <string>]
```

What it does depends on where you run it:

| Where | What happens |
| --- | --- |
| In a pane **without** a bar | Clears the pane, adds the bar and starts a new shell under it (the same shell you're using now, in the same folder). Type `exit` to remove the bar and return to your original shell. |
| In a pane **with** a bar | Updates the existing bar in place. Nothing restarts. |

### Parameters

| Parameter | Description |
| --- | --- |
| `-Title` | Bar text. Default when starting a bar: the current folder's name. |
| `-Color` | Bar background: a color name (see [Colors](#colors)) or `#RRGGBB`. Default: picked from the title text. |
| `-Foreground` | Text color, same formats. Default: black or white, whichever contrasts better. |
| `-Position` | `Top` (default) or `Bottom`. Only applies when starting a bar. See [Top or bottom](#top-or-bottom). |
| `-Command` | Run this command line under the bar instead of a new shell. The bar goes away when the command exits. Only applies when starting a bar. |

### Examples

```powershell
ptag                                         # bar titled after the current folder
ptag 'Backend API'                           # bar with a title
ptag 'Backend API' -Color green              # with a specific color
ptag 'Agent 1' -Command claude               # bar only while claude runs
ptag 'Agent 2' -Command 'claude --continue'  # arguments go in the same string
ptag 'Logs' -Position Bottom                 # bar at the bottom, keeps scrollback

# Inside a pane that already has a bar:
ptag 'Backend API (debugging)'               # rename
ptag -Color red                              # recolor only
ptag -Foreground '#000000'                   # change the text color
```

The exit code of the shell or command is passed back, so `$LASTEXITCODE` works after
`ptag -Command ...`.

## Renaming while an app is running

While claude, codex or another app is running, you can't type `ptag` in that pane.
Instead, use `panetag --rename` from **any shell inside the pane**. Every titled pane has
`panetag` on its PATH, so it works from PowerShell, cmd, and Git Bash (which Claude Code's
`!` commands use). It's instant: no PowerShell needs to start.

In Claude Code, type:

```
! panetag --rename Reviewing PR
! panetag --rename Reviewing PR -c red
! panetag --update -c green
```

Quotes are optional: all words after `--rename` become the title. You can also just ask
Claude to "rename this pane to Reviewing PR with panetag --rename".

| Command | Effect |
| --- | --- |
| `panetag --rename TEXT` | Rename the bar |
| `panetag --rename TEXT -c COLOR --fg COLOR` | Rename and recolor |
| `panetag --update -c COLOR` | Recolor only |
| `panetag --update --fg COLOR` | Change the text color only |

Outside a titled pane these print `not inside a PaneTag pane` and exit with code 1.

> Panes started before you upgraded to 0.2.0 don't have `panetag` on their PATH. Type
> `exit` and start the bar again, or call `panetag.exe` by its full path.

## `panetag.exe`

```
panetag [options] [--] [command [args...]]
panetag --rename TEXT [-c COLOR] [--fg COLOR]
panetag --update [-t TEXT] [-c COLOR] [--fg COLOR]
```

| Option | Description |
| --- | --- |
| `-t`, `--title TEXT` | Bar text. Default: the working folder's name. |
| `-c`, `--color COLOR` | Bar background (name or `#RRGGBB`). Default: picked from the title. |
| `--fg COLOR` | Text color. Default: black or white for contrast. |
| `-p`, `--position top\|bottom` | Where the bar goes. Default: `top`. |
| `--cwd DIR` | Working folder for the command. Default: the current folder. |
| `--cmd CMDLINE` | The full command line as a single string (alternative to trailing arguments). |
| `--title-b64`, `--cwd-b64`, `--cmd-b64` | Same as above, but base64-encoded UTF-8. The module uses these to avoid Windows PowerShell's quoting bugs. |
| `--rename TEXT` | Inside a titled pane: rename its bar, then exit. Extra words are appended to the title. See [Renaming while an app is running](#renaming-while-an-app-is-running). |
| `--update` | Inside a titled pane: apply `-t`, `-c` and `--fg` to its bar, then exit. |
| `-h`, `--help` | Show help. |

Everything after the options (or after `--`) is the command to run. Without a command it
runs `powershell.exe -NoLogo`.

```powershell
panetag.exe -t Backend -c blue                      # Windows PowerShell under a blue bar
panetag.exe -t Agent -- claude --continue           # claude under a bar
panetag.exe -t Shell -p bottom -- pwsh -NoLogo      # PowerShell 7, bar at the bottom
```

The exit code is the command's exit code, or `1` if `panetag.exe` itself failed to start
(the reason is printed to stderr).

## Environment variables

| Variable | Set by | Meaning |
| --- | --- | --- |
| `PANETAG_POSITION` | You | Default position (`top` or `bottom`) when none is given. |
| `PANETAG_COLOR` | You | Default bar color when none is given. |
| `PANETAG_PIPE` | `panetag.exe` | Set inside a titled pane. Names the pipe that `Set-PaneTag` and `panetag --rename` use to update the bar. Don't set it yourself. |
| `PATH` | `panetag.exe` | Inside a titled pane, `panetag.exe`'s folder is added to the end of `PATH`, so `panetag` works by name. Your PATH outside the pane is not changed. |

## Colors

Named colors:

| Name | Hex | | Name | Hex |
| --- | --- | --- | --- | --- |
| `blue` | `#1F6FEB` | | `indigo` | `#4F46E5` |
| `green` | `#1A7F37` | | `cyan` | `#0891B2` |
| `purple` | `#8250DF` | | `brown` | `#8B5E3C` |
| `orange` | `#D9730D` | | `gray` | `#57606A` |
| `red` | `#CF222E` | | `black` | `#24292F` |
| `teal` | `#0F8B8D` | | `white` | `#EAEEF2` |
| `pink` | `#BF3989` | | | |
| `yellow` | `#D4A72C` | | | |

Any `#RRGGBB` or `#RGB` value also works (the `#` is optional).

When you don't choose a color, one of the first eleven (blue to brown) is picked from the
title text. The same title always gets the same color, so a pane called "Backend" is the
same color every time. Renaming a bar re-picks the color unless you set one explicitly.

## What the bar shows

- **Left, bold:** your title.
- **Right:** the running app's own window title, for example `✳ Claude Code` or `powershell`.
  It's hidden when the bar is too narrow or when it matches your title.

The Windows Terminal **tab title** becomes `Your title · app title`, so the tab shows the
focused pane's title. When the bar is removed, the original tab title is restored.

## Top or bottom

| | Top (default) | Bottom |
| --- | --- | --- |
| Where the bar is | First row | Last row |
| Scrollback | **Lost.** Lines that scroll off the screen are discarded. | Kept, as normal. |
| Good for | Full-screen apps, short sessions | Shells and Claude Code sessions you scroll back through |

Why: Windows Terminal only adds lines to scrollback when the scrolling area starts at the
top row of the screen. A top bar means the scrolling area starts at row 2. See
[architecture.md](architecture.md#scrollback) for details.

## Windows Terminal integration

In the examples below, `panetag.exe` lives in the installed module folder. Adjust the
path if your Documents folder is elsewhere (see [Installing](#installing)).

### A profile that always has a bar

In Windows Terminal, open **Settings → Open JSON file** and add a profile:

```json
{
    "name": "Backend",
    "commandline": "%USERPROFILE%\\Documents\\WindowsPowerShell\\Modules\\PaneTag\\panetag.exe -t Backend -c blue -- powershell.exe -NoLogo",
    "startingDirectory": "E:\\Projects\\backend"
}
```

### Open a split pane with a bar from the command line

```powershell
$pt = "$HOME\Documents\WindowsPowerShell\Modules\PaneTag\panetag.exe"
wt -w 0 sp -V $pt -t Logs -c gray
```

`-w 0` targets the current window, `sp` is `split-pane`, `-V` splits vertically (`-H` for horizontal).

### A whole layout in one command

In PowerShell, `wt` subcommands are separated with an escaped semicolon (`` `; ``):

```powershell
$pt = "$HOME\Documents\WindowsPowerShell\Modules\PaneTag\panetag.exe"
wt -w 0 nt -d E:\Projects\app $pt -t Frontend -c blue -- claude `; `
   sp -V -d E:\Projects\app $pt -t Backend -c green -- claude `; `
   sp -H -d E:\Projects\app $pt -t Tests -c orange
```

This opens a new tab with three panes: two Claude Code agents and a shell for running tests,
each with its own bar.

### A key binding for a titled split

In `settings.json`, under `"actions"`:

```json
{
    "command": {
        "action": "splitPane",
        "split": "auto",
        "commandline": "%USERPROFILE%\\Documents\\WindowsPowerShell\\Modules\\PaneTag\\panetag.exe -t Scratch"
    },
    "keys": "alt+shift+t"
}
```

You can rename it afterwards with `ptag` inside the new pane.

## Keyboard and mouse

- All keys, including **Ctrl+C**, go to the app inside the pane. Ctrl+Break doesn't close the bar.
- Paste, focus events and mouse reporting work as usual. With a top bar, mouse positions are
  adjusted so apps get the right row, and clicks on the bar itself are ignored.
- Resizing the pane or the window resizes the app, and the bar is redrawn within about 0.1 seconds.
