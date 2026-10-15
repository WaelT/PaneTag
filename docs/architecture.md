# Architecture

This explains how `panetag.exe` keeps a bar on screen that apps can't remove, and why
it's built this way. The code is in [`src/PaneTag.cs`](../src/PaneTag.cs).

## The problem

Windows Terminal (WT) has no per-pane title bars (as of 1.24). The only per-pane text
is the window title, and apps such as Claude Code overwrite it constantly. Anything you
print into the pane yourself is wiped by the first app that clears the screen or switches
to full-screen mode.

So the bar has to live in a part of the screen the app can't reach.

## The approach: a pane-within-a-pane

`panetag.exe` sits between Windows Terminal and your shell:

```
Windows Terminal pane (R rows)
 └─ panetag.exe ─────────── draws the bar on the reserved row
     └─ pseudo console (R-1 rows)
         └─ powershell / claude / codex ...
```

1. It creates a Windows **pseudo console** (ConPTY) that is **one row shorter** than the pane,
   and starts the shell or command inside it. The app sees a terminal with R-1 rows and
   lays itself out for that.
2. Everything the app writes comes out of the pseudo console as a stream of text and
   **escape sequences**: short codes such as "move the cursor to row 5, column 1" or
   "clear the screen". `panetag.exe` reads that stream, **rewrites** the few sequences
   that refer to rows, and writes the result to the real pane.
3. It sets the real terminal's **scrolling region** (margins) to exclude the bar row, so
   ordinary scrolling never moves the bar.
4. It draws the bar on the reserved row, and redraws it after anything that could have
   wiped it.

This is the same idea as the status line in `tmux` or `screen`, without a full terminal
emulator in the middle. That keeps it small, fast and faithful: colors, Unicode,
hyperlinks, mouse support and so on pass through untouched.

### Coordinates

| Name | Meaning |
| --- | --- |
| `rows`, `cols` | Size of the real pane |
| `InnerRows` | `rows - 1`: what the app sees |
| `Offset` | `1` for a top bar, `0` for a bottom bar. Added to every row number the app uses. |
| `HeaderRow` | `1` for a top bar, `rows` for a bottom bar |

## Threads

| Thread | Job |
| --- | --- |
| Main | Sets up the console and pseudo console, starts the command, waits for it to exit, then cleans up. |
| `OutputLoop` | Reads the pseudo console's output, rewrites it, decides whether the bar needs redrawing, writes to the pane. |
| `InputLoop` | Reads keyboard/mouse input from the pane (as escape sequences, UTF-16), adjusts row numbers in mouse and cursor reports, and writes it to the pseudo console as UTF-8. |
| `PollLoop` | Every 100 ms: checks the pane size and resizes the pseudo console if it changed; redraws the bar if a redraw has been pending for more than 250 ms. |
| `PipeLoop` | Serves the named pipe that `Set-PaneTag` uses to rename or recolor the bar. |

A single lock (`gate`) protects all terminal state and every write to the pane, so the
bar is never drawn in the middle of the app's output.

## Output rewriting

`Process()` is a byte-level state machine that splits the stream into plain text and
escape sequences. It keeps partial sequences across reads, so a sequence split across two
reads is handled correctly. Plain text and most sequences are copied through unchanged.
These are the exceptions:

| Sequence | Name | What `panetag.exe` does |
| --- | --- | --- |
| `CSI row ; col H` / `f` | Cursor position | Clamps the row to `1..InnerRows` and adds `Offset`. Skipped when the app uses origin mode, because then rows are already relative to the margins. |
| `CSI row d` | Line position | Same as above. |
| `CSI top ; bottom r` | Set scrolling region | Remembers the app's region, then sets the real region shifted by `Offset` and clamped to the app's area. Moves the cursor to the app's home position, since this sequence homes the cursor and the real home might be the bar row. Invalid regions are dropped, as a terminal would ignore them. |
| `CSI J` | Erase in display | Passed through; the bar is marked for redraw. |
| `CSI ? 47/1047/1049 h/l` | Alternate screen | Passed through; the bar and margins are marked for redraw (the other screen buffer is blank). |
| `CSI ? 6 h/l` | Origin mode | Tracked (affects cursor-position rewriting). |
| `CSI ? 7 h/l` | Auto-wrap | Tracked (restored after drawing the bar). |
| `ESC c` | Full reset | Passed through, then margins re-applied and the cursor moved below the bar; app state is reset. |
| `CSI ! p` | Soft reset | Passed through; margins and bar marked for redraw. |
| `ESC # 8` | Screen alignment test | Fills the screen, so the bar is marked for redraw. |
| `CSI 6 n`, `CSI ? 6 n` | Cursor position request | Passed through and counted, so the reply can be adjusted (see [Input](#input-rewriting)). |
| `ESC 7`/`ESC 8`, `CSI s`/`CSI u` | Save/restore cursor | Tracked (see [Drawing the bar](#drawing-the-bar)). |
| `OSC 0 ; text` / `OSC 2 ; text` | Window title | **Not** passed through. The text becomes the app title on the right of the bar, and the tab title is set to `Your title · app title`. |

Device-control strings, other OSC sequences (hyperlinks, clipboard, progress, shell
integration) and everything else go through untouched.

## Drawing the bar

The bar is drawn by `TryFlushHeader()`, and only when it's safe:

- the parser is between sequences and not halfway through a multi-byte UTF-8 character
  (otherwise the bar's bytes would corrupt the app's output);
- the app has no saved cursor outstanding. Drawing uses save/restore cursor
  (`ESC 7`/`ESC 8`), and the terminal has only one save slot per screen. If the app saved
  its cursor and hasn't restored it yet, redrawing would overwrite its saved position.
  In that case the redraw waits, and `PollLoop` forces it after 250 ms.

The drawing sequence is:

```
OSC 0 ; tab title BEL         set the tab title (when it changed)
ESC 7                         save cursor, colors and modes
CSI top ; bottom r            re-apply margins (when needed)
CSI ? 6 l                     leave origin mode (only if the app had it on)
CSI ? 7 l                     no auto-wrap, so a long title can't spill onto the next row
CSI HeaderRow ; 1 H           go to the bar row
CSI 0;1;38;2;…;48;2;… m       bold, text and background color
CSI 2 K                       paint the whole row in the bar color
 title                        left side
CSI col G  app title          right-aligned app title
CSI 0 m, CSI ? 7 h            reset colors, restore auto-wrap if the app had it on
ESC 8                         restore cursor, colors and origin mode
```

Clearing the whole row in the bar color and placing the right-hand text by column means
the bar always fills the full width, even if a character's width was estimated wrongly.

## Input rewriting

Keys and pastes pass through untouched. With a top bar, two kinds of report coming *from*
the terminal contain absolute row numbers that are one too high for the app:

- **Cursor position reports** (`CSI row ; col R`): adjusted only while the app has a
  request outstanding, because the same pattern is also a key code (Ctrl+F3 and similar).
- **SGR mouse reports** (`CSI < button ; x ; y M/m`): the row is adjusted. Clicks on the
  bar row are dropped.

Input is read with `ReadConsoleW` (UTF-16) and encoded to UTF-8 with a stateful encoder,
so characters split across reads stay intact.

## Rename pipe

When it starts, `panetag.exe` creates the named pipe `\\.\pipe\panetag-<pid>` and
puts its name in `PANETAG_PIPE` for the child. Anything running in the pane can send
UTF-8 lines to it:

| Line | Effect |
| --- | --- |
| `title <text>` | Change the title (and the automatic color, unless a color was set explicitly). |
| `color <name or #RRGGBB>` | Change the background. |
| `fg <name or #RRGGBB>` | Change the text color. |

Unknown lines and invalid colors are ignored. The pipe accepts one client at a time, and
uses the default security settings, so only the same user can connect.

Example without the module:

```powershell
$p = New-Object IO.Pipes.NamedPipeClientStream('.', $env:PANETAG_PIPE, 'Out')
$p.Connect(1000); $w = New-Object IO.StreamWriter($p); $w.WriteLine('title Hello'); $w.Dispose()
```

## Startup and exit

**Startup:**

1. Check that input and output are a real console, and read the pane size.
2. Turn on escape-sequence processing for output and escape-sequence input for keys,
   switch output to UTF-8, and ignore Ctrl+C/Ctrl+Break signals (Ctrl+C arrives as a
   normal key and goes to the app).
3. Create the pipes and pseudo console (R-1 rows), then start the command with null
   standard handles so it uses the pseudo console rather than inheriting the wrapper's.
4. Clear the pane, set the margins, draw the bar, and put the cursor at the app's top-left.

**Exit** (when the command exits):

1. Close the pseudo console. It flushes its final output, which is still processed.
2. Leave the alternate screen if the app left it on, turn off mouse reporting, bracketed
   paste and origin mode, show the cursor and turn auto-wrap back on, in case the app
   crashed without cleaning up.
3. Reset the margins, clear the bar row and restore the original tab title.
4. Restore the console modes and code page, and exit with the command's exit code.

## Scrollback

When a line feed happens at the bottom of the scrolling region, Windows Terminal
(`AdaptDispatch::_DoLineFeed` in microsoft/terminal) only moves the top line into
scrollback if the region **starts at the top of the screen**. Rows below the region are
kept in place.

- **Bottom bar:** region = rows 1 to R-1. It starts at the top, so scrolled lines go into
  scrollback, and the bar below the region stays put. Scrollback works.
- **Top bar:** region = rows 2 to R. Scrolling inside it discards the top line.
  Scrollback is lost.

Keeping scrollback with a top bar would require a full terminal emulator with its own
scrollback buffer and scrolling UI (as `tmux` does), which is outside the scope of this
project. That's why both positions are offered.

## Design decisions

| Decision | Why |
| --- | --- |
| Pseudo console wrapper instead of printing a banner | Apps clear the screen and use the alternate screen; only a reserved row survives. |
| Rewrite the stream instead of running a full terminal emulator | Much smaller, adds no lag, and passes through every feature the app uses. The cost is losing scrollback with a top bar. |
| C# 5 compiled with the `csc.exe` built into Windows | Works on any Windows machine without installing an SDK. `build.ps1` is the whole build. |
| Base64 arguments from the module | Windows PowerShell 5.1 mangles double quotes in arguments to native programs. |
| Named pipe for renaming | Works from any process in the pane. The pseudo console may filter unknown escape sequences, so an in-band escape-sequence protocol wasn't reliable. |
| Colors picked from a hash of the title | Panes get distinct, stable colors with no configuration. |
