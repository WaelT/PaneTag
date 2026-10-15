# Troubleshooting

## `ptag` is not recognized

- Run `.\install.ps1`, then open a **new** pane. Already-open sessions don't see new modules.
- Check that PowerShell can find the module:
  ```powershell
  Get-Module -ListAvailable PaneTag
  ```
  If nothing is listed, check that the install folder is in `$env:PSModulePath`.
- If you copied the files from another machine or a download, Windows may have blocked them:
  ```powershell
  Get-ChildItem (Get-Module -ListAvailable PaneTag).ModuleBase | Unblock-File
  ```

## "panetag: stdin/stdout must be a console"

`panetag.exe` must run directly in a terminal. It fails when its input or output is
redirected (`| ...`, `> file`) or in hosts without a real console, such as the PowerShell
ISE. Use Windows Terminal. Other ConPTY-based terminals, such as VS Code's, should work but are untested.

## "panetag.exe not found next to the module"

The module folder is missing the helper. Run `.\build.ps1` (or `.\install.ps1` again).

## I can't scroll back to earlier output

You're using a top bar. Windows Terminal discards lines that scroll out of a scrolling
area that doesn't start at the top row. Use a bottom bar instead:

```powershell
ptag 'My title' -Position Bottom
$env:PANETAG_POSITION = 'bottom'   # in $PROFILE to make it the default
```

See [architecture.md](architecture.md#scrollback) for why.

## Renaming does nothing, or "Cannot reach this pane's title bar"

`Set-PaneTag` renames through the pipe named in `$env:PANETAG_PIPE`. That fails if
the variable was inherited by a process that outlived its bar (for example a shell started
from a titled pane and then moved elsewhere). Clear it and start a new bar:

```powershell
Remove-Item Env:PANETAG_PIPE
ptag 'New title'
```

## Two bars in one pane

You ran `panetag.exe` directly inside a pane that already had a bar. `ptag` avoids
this by renaming instead. Type `exit` to leave the inner one.

## The bar flickers or briefly disappears

The bar is redrawn right after an app clears the screen or switches screens, normally in the
same screen update. If an app has saved its cursor position, the redraw waits up to
250 ms so the app's saved position isn't overwritten, so it can briefly vanish. Resizing
redraws within about 100 ms.

## The bar's text is cut off or the app title is missing

The app title on the right is only shown when there are at least 6 spare columns. Long
titles are shortened with `…`. Widen the pane or shorten the title.

## Colors look wrong

The bar uses 24-bit color. Windows Terminal supports it. In other terminals, try a
different named color or a `#RRGGBB` value they render well.

## The terminal is in a strange state after an app crashed

`panetag.exe` resets the common modes (mouse reporting, bracketed paste, alternate
screen, cursor visibility, auto-wrap) when the command exits. If something is still off,
run a soft reset:

```powershell
[Console]::Write("$([char]27)[!p")
```

or close the pane.

## Ctrl+C doesn't remove the bar

That's intended: Ctrl+C goes to the app in the pane, the same as without a bar. Type `exit`
in the shell, or quit the app if you used `-Command`.

## Reporting a problem

Please include:

- Windows version (`winver`) and Windows Terminal version
- the exact command you ran and the bar position
- the app you were running (and its version)
- what you expected and what you saw (a screenshot helps)

If you can reproduce it headlessly, the smoke-test harness can capture the exact output.
See [development.md](development.md#testing).
