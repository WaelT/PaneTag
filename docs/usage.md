# Usage

There is one command to remember: **`ptag`**.

- At a PowerShell prompt, `ptag` is the module's `Set-PaneTag` command.
- In any other shell inside a titled pane (cmd, Git Bash, Claude Code's `!` commands),
  `ptag` is `ptag.exe`, a copy of the program.

Both behave the same: in a pane without a bar, `ptag Title` adds one; in a pane with a
bar, `ptag New title` renames it. `panetag.exe` is the same program under its full name,
for Windows Terminal profiles, key bindings and `wt` layout scripts.

## Installing

### From the PowerShell Gallery

```powershell
Install-Module PaneTag -Scope CurrentUser       # or: Install-PSResource PaneTag
```

Run it in each PowerShell you use (Windows PowerShell 5.1 and PowerShell 7 keep separate
module folders). To upgrade: `Update-Module PaneTag`. To remove: `Uninstall-Module PaneTag`.

The package includes prebuilt `panetag.exe` and `ptag.exe`. They aren't code-signed; if
your antivirus objects, install from source instead.

### From source

```powershell
git clone https://github.com/WaelT/PaneTag.git
cd PaneTag
.\install.ps1
```

This builds `PaneTag\panetag.exe` (plus its `ptag.exe` copy) with the C# compiler built
into Windows and copies the `PaneTag` module folder to:

- `Documents\WindowsPowerShell\Modules\PaneTag` (Windows PowerShell 5.1)
- `Documents\PowerShell\Modules\PaneTag` (PowerShell 7)

To uninstall, delete those two `PaneTag` folders.

### After installing

PowerShell loads the module automatically the first time you type `ptag` or
`Set-PaneTag`, so you don't need to edit your profile. Open a new pane after installing.

To find where it was installed (Gallery installs use a versioned folder such as
`PaneTag\0.3.0`, and Documents may be redirected to OneDrive):

```powershell
(Get-Module -ListAvailable PaneTag).ModuleBase
```

If you use the [Claude Code hooks](#claude-code-see-which-agent-needs-you), run
`Enable-PaneTagClaudeHooks` again after upgrading: the hooks point to `ptag.exe` in that folder.

## `Set-PaneTag` / `ptag`

```
Set-PaneTag [[-Title] <string[]>] [-Color <string>] [-Foreground <string>]
            [-Position Top|Bottom] [-Command <string>]
            [-Status <string>] [-StatusColor <string>] [-Sticky] [-ClearStatus]
```

What it does depends on where you run it:

| Where | What happens |
| --- | --- |
| In a pane **without** a bar | Clears the pane, adds the bar and starts a new shell under it (the same shell you're using now, in the same folder). Type `exit` to remove the bar and return to your original shell. |
| In a pane **with** a bar | Updates the existing bar in place. Nothing restarts. |

### Parameters

| Parameter | Description |
| --- | --- |
| `-Title` | Bar text. Several words are joined, so quotes are optional. Default when starting a bar: the current folder's name. |
| `-Color` | Bar background: a color name (see [Colors](#colors)) or `#RRGGBB`. Default: picked from the title text. |
| `-Foreground` | Text color, same formats. Default: black or white, whichever contrasts better. |
| `-Position` | `Top` (default) or `Bottom`. Only applies when starting a bar. See [Top or bottom](#top-or-bottom). |
| `-Command` | Run this command line under the bar instead of a new shell. The bar goes away when the command exits. Only applies when starting a bar. |
| `-Status` | Flag the bar with a label; see [Status flags](#status-flags). |
| `-StatusColor` | Color while flagged. Default: `orange`. |
| `-Sticky` | Keep the status until `-ClearStatus` instead of clearing it on the next keypress. |
| `-ClearStatus` | Remove the status. |

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

While claude, codex or another app is running, the pane's PowerShell prompt is busy. Use
`ptag` from the app's own shell instead: every titled pane has `ptag` on its PATH, so it
works from cmd and Git Bash too. In Claude Code, `!` runs a command:

```
! ptag Reviewing PR
! ptag Reviewing PR -c red
! ptag -c green
! ptag --fg black
```

Quotes are optional: all plain words become the title. It's instant, because no
PowerShell has to start. You can also just ask Claude to "rename this pane to Reviewing PR
with ptag".

Inside a pane that has a bar, `ptag` never starts a second one. Options that only apply
when starting a bar (`-p`, `--cwd`, or a command after `--`) are refused with
`this pane already has a bar`.

> Panes started before you upgraded to 0.2.0 don't have `ptag` on their PATH. Type
> `exit` and start the bar again.

## Status flags

A status temporarily recolors the bar and adds a label, without changing its title or
color:

```
ptag --status Your turn                  # orange, "Backend ● Your turn"
ptag --status Build failed --status-color red --sticky
ptag --clear-status
```

- The status **clears on your next keypress in that pane**, so it works as a "look here"
  signal: once you start typing, it goes away. Mouse clicks, focus changes and pressing only
  Shift/Ctrl/Alt don't count.
- `--sticky` keeps it until `--clear-status`.
- The tab title shows it too: `Backend ● Your turn · Claude Code`.
- Outside a titled pane, `--status` and `--clear-status` quietly do nothing (exit code 0),
  so they're safe to call from hooks and scripts that also run elsewhere.

In PowerShell the same options are `-Status`, `-StatusColor`, `-Sticky` and `-ClearStatus`.

## Claude Code: see which agent needs you

With several Claude Code sessions in titled panes, PaneTag can flag the pane whose agent is
waiting for you:

| When | Bar |
| --- | --- |
| Claude finishes and it's your turn | **orange**, `● Your turn` |
| Claude asks for permission | **red**, `● Needs you` |
| You type in the pane, or send a prompt | back to normal |

Turn it on once:

```powershell
Enable-PaneTagClaudeHooks
```

This adds three [hooks](https://code.claude.com/docs/en/hooks) to your Claude Code user
settings (`~/.claude/settings.json`):

| Hook | Runs |
| --- | --- |
| `Stop` | `ptag.exe --status "Your turn" --status-color orange` |
| `Notification` (matcher `permission_prompt`) | `ptag.exe --status "Needs you" --status-color red` |
| `UserPromptSubmit` | `ptag.exe --clear-status` |

Details:

- Your existing settings and hooks are kept, and the file is backed up first
  (`settings.json.bak-<date>`). Running it again replaces PaneTag's hooks without duplicating them.
- The hooks call `ptag.exe` from the module folder by full path, with no shell in between,
  so they work whether Claude Code uses Git Bash or PowerShell. They run in the background
  (`async`), so Claude never waits for them.
- In Claude sessions outside a titled pane they do nothing.
- Choose other colors with `-YourTurnColor` and `-NeedsYouColor`. To enable it for one
  project only, pass `-SettingsPath .\.claude\settings.json`.
- **Run it again after upgrading or moving the module**, because the hooks contain the path
  to `ptag.exe`.

To turn it off:

```powershell
Disable-PaneTagClaudeHooks
```

## `panetag.exe` / `ptag.exe`

```
panetag [TITLE...] [options] [-- command [args...]]
```

In a pane without a bar it starts one. In a pane with a bar it changes that bar (title,
`-c`, `--fg`) and exits.

| Option | Description |
| --- | --- |
| `TITLE...` | Bar text; all plain words are joined, so quotes are optional. Default: the working folder's name. |
| `-t`, `--title TEXT` | Bar text, as an option (use it when the title starts with `-`). |
| `-c`, `--color COLOR` | Bar background (name or `#RRGGBB`). Default: picked from the title. |
| `--fg COLOR` | Text color. Default: black or white for contrast. |
| `-s`, `--status TEXT` | Flag the bar with a status label (words after it are appended). See [Status flags](#status-flags). |
| `--status-color COLOR` | Status color. Default: `orange`. |
| `--sticky` | Keep the status until `--clear-status`. |
| `--clear-status` | Remove the status. |
| `-p`, `--position top\|bottom` | Where the bar goes. Default: `top`. |
| `--cwd DIR` | Working folder for the command. Default: the current folder. |
| `--cmd CMDLINE` | The full command line as a single string (alternative to `-- command`). |
| `--title-b64`, `--cwd-b64`, `--cmd-b64` | Same as above, but base64-encoded UTF-8. The module uses these to avoid Windows PowerShell's quoting bugs. |
| `-h`, `--help` | Show help. |

The command to run goes after `--`. Without one, it runs `powershell.exe -NoLogo`.
`-p`, `--cwd`, `--cmd` and the command only apply when starting a bar.

```powershell
panetag.exe Backend -c blue                         # Windows PowerShell under a blue bar
panetag.exe Agent -- claude --continue              # claude under a bar
panetag.exe Shell -p bottom -- pwsh -NoLogo         # PowerShell 7, bar at the bottom
```

The exit code is the command's exit code, or `1` if `panetag.exe` itself failed to start
(the reason is printed to stderr).

## Environment variables

| Variable | Set by | Meaning |
| --- | --- | --- |
| `PANETAG_POSITION` | You | Default position (`top` or `bottom`) when none is given. |
| `PANETAG_COLOR` | You | Default bar color when none is given. |
| `PANETAG_PIPE` | `panetag.exe` | Set inside a titled pane. Names the pipe that `ptag` uses to update the bar, and tells `ptag` it's inside a titled pane. Don't set it yourself. |
| `PATH` | `panetag.exe` | Inside a titled pane, `panetag.exe`'s folder is added to the end of `PATH`, so `ptag` and `panetag` work by name in any shell. Your PATH outside the pane is not changed. |

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

The examples below need the path to `panetag.exe` in the installed module folder. Get it with:

```powershell
Join-Path (Get-Module -ListAvailable PaneTag | Select-Object -First 1).ModuleBase panetag.exe
```

The JSON examples use the from-source location; a Gallery install adds a version folder
(`...\Modules\PaneTag\0.3.0\panetag.exe`), so update the path after upgrading.

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
$pt = Join-Path (Get-Module -ListAvailable PaneTag | Select-Object -First 1).ModuleBase panetag.exe
wt -w 0 sp -V $pt -t Logs -c gray
```

`-w 0` targets the current window, `sp` is `split-pane`, `-V` splits vertically (`-H` for horizontal).

### A whole layout in one command

In PowerShell, `wt` subcommands are separated with an escaped semicolon (`` `; ``):

```powershell
$pt = Join-Path (Get-Module -ListAvailable PaneTag | Select-Object -First 1).ModuleBase panetag.exe
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
