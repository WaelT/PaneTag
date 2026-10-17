# Headless smoke tests: runs panetag.exe inside a pseudo console (tests\PtyHarness.cs)
# and checks the rendered output. Builds panetag.exe first unless -NoBuild is given.
param([switch]$NoBuild)
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
if (-not $NoBuild) { & (Join-Path $root 'build.ps1') }

# Inside a titled pane panetag would update that bar instead of starting test bars.
# Restored in the finally block at the end, so running the tests in your own titled pane
# doesn't break ptag there.
$savedPipe = $env:PANETAG_PIPE
Remove-Item Env:PANETAG_PIPE -ErrorAction SilentlyContinue
try {

$exe = Join-Path $root 'PaneTag\panetag.exe'
$module = Join-Path $root 'PaneTag\PaneTag.psd1'
$work = Join-Path $env:TEMP 'panetag-tests'
New-Item -ItemType Directory -Force $work | Out-Null
$harness = Join-Path $work 'PtyHarness.exe'
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $csc /nologo /r:System.Core.dll "/out:$harness" (Join-Path $PSScriptRoot 'PtyHarness.cs')
if ($LASTEXITCODE -ne 0) { throw 'Harness build failed' }

function B64([string]$s) { [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($s)) }
function Enc([string]$s) { [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($s)) }

# Runs the harness; command lines are base64-encoded so Windows PowerShell can't mangle quotes.
function Invoke-Pty([string]$CommandLine, [string]$Rename, [string]$Keys) {
    $harnessArgs = @('60', '10', '15000', $CommandLine)
    if ($Rename -or $Keys) { if ($Rename) { $harnessArgs += $Rename } else { $harnessArgs += '-' } }
    if ($Keys) { $harnessArgs += $Keys }
    (& $harness @harnessArgs) -join "`n"
}

$failures = 0
function Check([string]$Name, [bool]$Ok, [string]$Output) {
    if ($Ok) { Write-Host "PASS  $Name" -ForegroundColor Green }
    else {
        Write-Host "FAIL  $Name" -ForegroundColor Red
        Write-Host $Output
        $script:failures++
    }
}

# 1. Bar on row 1, child sees one row less, alt screen + clear redraw the bar, exit code passes through.
$child = 'powershell -NoProfile -Command "$e=[char]27; [Console]::Write($e+''[?1049h''+$e+''[1;1HTOPLEFT''); ' +
         'Start-Sleep -Milliseconds 500; [Console]::Write($e+''[2J''); Start-Sleep -Milliseconds 500; ' +
         '[Console]::Write($e+''[?1049l''); mode con | findstr Lines; exit 7"'
$out = Invoke-Pty "`"$exe`" -t Smoke --cmd-b64 $(B64 $child)"
Check 'bar is drawn' ($out -match '\\e\[H Smoke ') $out
Check 'child sees rows - 1' ($out -match 'Lines:\s+9\b') $out
Check 'child row 1 lands below the bar' ($out -match '(?s)\\e\[2;1H(?:(?!\\e\[\d+;\d+H).)*TOPLEFT') $out
Check 'bar redrawn at least 3 times' (([regex]::Matches($out, ' Smoke ')).Count -ge 3) $out
Check 'exit code passes through' ($out -match '<<<EXIT 7>>>') $out

# 2. Rename over the pipe.
#    Plain words are the title when starting a bar.
$out = Invoke-Pty "`"$exe`" Two Words --cmd-b64 $(B64 'powershell -NoProfile -Command "Start-Sleep 3"')" 'After Rename'
Check 'plain words are the title' ($out -match ' Two Words ') $out
Check 'rename over pipe' (($out -match '<<<RENAME SENT>>>') -and ($out -match ' After Rename ')) $out

# 3. Module: launch with -Command, rename from inside, exit code reaches the outer shell.
$inner = "Import-Module '$module'; Set-PaneTag 'Renamed Inside' -Color orange; Start-Sleep -Milliseconds 800; exit 3"
$outer = "Import-Module '$module'; Set-PaneTag 'Module Test' -Color purple -Command 'powershell -NoProfile -EncodedCommand $(Enc $inner)'; 'outer exit=' + `$LASTEXITCODE"
$out = Invoke-Pty "powershell -NoProfile -EncodedCommand $(Enc $outer)"
Check 'module starts the bar' ($out -match ' Module Test ') $out
Check 'module renames from inside' ($out -match ' Renamed Inside ') $out
Check 'module returns exit code' ($out -match 'outer exit=3') $out

# 4. `ptag New title` by bare name inside a pane (the pane puts ptag on PATH): unquoted
#    words become the title, -c recolors.
$child = 'cmd /c "ptag Via Cmd -c red & ping -n 3 127.0.0.1 >nul"'
$out = Invoke-Pty "`"$exe`" -t Before --cmd-b64 $(B64 $child)"
Check 'ptag renames from cmd' (($out -match ' Via Cmd ') -and ($out -match '48;2;207;34;46m')) $out

$bash = Join-Path $env:ProgramFiles 'Git\bin\bash.exe'
if (Test-Path $bash) {
    $child = "`"$bash`" -c `"ptag Via Bash; sleep 2`""
    $out = Invoke-Pty "`"$exe`" -t Before --cmd-b64 $(B64 $child)"
    Check 'ptag renames from Git Bash (Claude Code ! commands)' ($out -match ' Via Bash ') $out
}
else { Write-Host 'SKIP  ptag renames from Git Bash (Git Bash not installed)' -ForegroundColor Yellow }

# 5. Inside a pane, starting a second bar is refused instead of nesting.
$child = 'cmd /c "ptag Nested -- cmd /c echo inner || echo REFUSED & ping -n 2 127.0.0.1 >nul"'
$out = Invoke-Pty "`"$exe`" -t Outer --cmd-b64 $(B64 $child)"
Check 'second bar in a pane is refused' (($out -match 'already has a bar') -and ($out -match 'REFUSED') -and ($out -notmatch ' Nested ')) $out

# 6. Status: shown in the status color, cleared by the next keypress (the harness types at 3 s).
#    A bar without a status shows the title directly followed by an escape sequence.
$child = 'cmd /c "ptag --status Your turn & ping -n 6 127.0.0.1 >nul"'
$out = Invoke-Pty "`"$exe`" -t Before --cmd-b64 $(B64 $child)" -Keys 'x'
$parts = $out -split '<<<TYPED>>>'
Check 'status is shown in orange' (($parts[0] -match 'Your turn') -and ($parts[0] -match '48;2;217;115;13m')) $out
Check 'keypress clears the status' (($parts.Count -eq 2) -and ($parts[1] -match ' Before \\e')) $out

# 7. --sticky survives keypresses until --clear-status.
$child = 'cmd /c "ptag --status Busy --sticky & ping -n 6 127.0.0.1 >nul & echo CLEARING & ptag --clear-status & ping -n 3 127.0.0.1 >nul"'
$out = Invoke-Pty "`"$exe`" -t Before --cmd-b64 $(B64 $child)" -Keys 'x'
$afterKey = ($out -split '<<<TYPED>>>')[-1]
$beforeClear, $afterClear = $afterKey -split 'CLEARING', 2
Check 'sticky status survives a keypress' (($out -match 'Busy') -and ($beforeClear -notmatch ' Before \\e')) $out
Check '--clear-status removes it' ($afterClear -match ' Before \\e') $out

# 7b. Stamped status updates (async hooks) apply in stamp order, not arrival order, and a
#     non-sticky status stamped before the last keypress (the harness types at 3 s) is dropped.
$send = 'function Send($l) { $p = New-Object IO.Pipes.NamedPipeClientStream(''.'', $env:PANETAG_PIPE, ''Out''); $p.Connect(2000); $w = New-Object IO.StreamWriter($p); $l | ForEach-Object { $w.WriteLine($_) }; $w.Dispose() }; '
#     (Parenthesize 'stamp ' + n: PowerShell's comma binds tighter than +.)
$inner = $send + '$t = [DateTime]::UtcNow.ToFileTimeUtc(); ' +
         'Send @((''stamp '' + ($t - 2)), ''status ''); Send @((''stamp '' + ($t - 3)), ''status Stale''); ' +
         'Send @((''stamp '' + ($t - 1)), ''status Fresh''); Start-Sleep 4; ' +
         'Send @((''stamp '' + $t), ''status Late''); Start-Sleep -Milliseconds 500; ' +
         'Send @((''stamp '' + [DateTime]::UtcNow.ToFileTimeUtc()), ''status After''); Start-Sleep 1'
$out = Invoke-Pty "`"$exe`" -t Before --cmd-b64 $(B64 "powershell -NoProfile -EncodedCommand $(Enc $inner)")" -Keys 'x'
Check 'older status update is dropped' (($out -match 'Fresh') -and ($out -notmatch 'Stale')) $out
Check 'status from before a keypress is dropped' (($out -match 'After') -and ($out -notmatch 'Late')) $out

# 7c. --timer: the label shows the age after a minute, keeps it when the same status is
#     raised again, uses hours past 60 minutes, and is absent without a timer.
$inner = $send + '$m = [TimeSpan]::TicksPerMinute; $t = [DateTime]::UtcNow.ToFileTimeUtc(); ' +
         'Send @((''timer '' + ($t - 5 * $m)), ''status Needs you''); Start-Sleep -Milliseconds 500; ' +
         'Send @((''timer '' + $t), ''status Needs you''); Start-Sleep -Milliseconds 500; ' +
         'Send @((''timer '' + ($t - 65 * $m)), ''status Waiting''); Start-Sleep -Milliseconds 500; ' +
         'Send @(''timer 0'', ''status Plain''); Start-Sleep -Milliseconds 500; ' +
         'Send @((''timer '' + ([DateTime]::UtcNow.ToFileTimeUtc() - 595000000)), ''status Ticking''); Start-Sleep 2'
$out = Invoke-Pty "`"$exe`" -t Before --cmd-b64 $(B64 "powershell -NoProfile -EncodedCommand $(Enc $inner)")"
Check 'timer shows minutes' ($out -match 'Needs you \S{1,3} 5m') $out
Check 'raising the same status keeps its age' ((([regex]::Matches($out, 'Needs you \S{1,3} 5m')).Count -ge 2) -and ($out -notmatch 'Needs you \\e')) $out
Check 'timer shows hours' ($out -match 'Waiting \S{1,3} 1h 05m') $out
Check 'no timer without --timer' ($out -match 'Plain \\e') $out
Check 'timer ticks over by itself' (($out -match 'Ticking \\e') -and ($out -match 'Ticking \S{1,3} 1m')) $out
$err = & $exe --status x --timer 2>&1 | Out-String
Check '--timer is accepted' (($LASTEXITCODE -eq 0) -and -not $err.Trim()) $err

# 8. Status outside a titled pane is a silent no-op (safe in hooks).
$ErrorActionPreference = 'Continue'
$err = & $exe --status Nope 2>&1 | Out-String
$ErrorActionPreference = 'Stop'
Check 'status outside a pane is a no-op' (($LASTEXITCODE -eq 0) -and -not $err.Trim()) $err

# 9. Claude Code hooks installer keeps existing settings, doesn't duplicate, removes cleanly.
$settingsFile = Join-Path $work 'settings.json'
Get-ChildItem "$work\settings.json*" -ErrorAction SilentlyContinue | Remove-Item
'{"model":"opus","hooks":{"Stop":[{"hooks":[{"type":"command","command":"echo done"}]}]}}' | Set-Content -Encoding ASCII $settingsFile
Import-Module $module -Force
Enable-PaneTagClaudeHooks -SettingsPath $settingsFile | Out-Null
Enable-PaneTagClaudeHooks -SettingsPath $settingsFile | Out-Null
$s = Get-Content -Raw $settingsFile | ConvertFrom-Json
$ours = @($s.hooks.Stop) | Where-Object { $_.hooks[0].command -match 'ptag\.exe$' }
Check 'hooks: added once, existing kept' ((@($s.hooks.Stop).Count -eq 2) -and (@($ours).Count -eq 1) -and ($s.model -eq 'opus') -and ($s.hooks.Notification[0].matcher -eq 'permission_prompt') -and ($s.hooks.PreToolUse[0].hooks[0].args -contains '--clear-status') -and ($s.hooks.Notification[0].hooks[0].args -contains '--timer')) ($s | ConvertTo-Json -Depth 10)
Check 'hooks: original backed up' (@(Get-ChildItem "$settingsFile.bak-*").Count -eq 2) ''
Disable-PaneTagClaudeHooks -SettingsPath $settingsFile | Out-Null
$s = Get-Content -Raw $settingsFile | ConvertFrom-Json
Check 'hooks: removed cleanly' ((@($s.hooks.PSObject.Properties).Count -eq 1) -and ($s.hooks.Stop[0].hooks[0].command -eq 'echo done')) ($s | ConvertTo-Json -Depth 10)

if ($failures -gt 0) { throw "$failures check(s) failed" }
Write-Host 'All smoke tests passed.' -ForegroundColor Green
exit 0
}
finally {
    $env:PANETAG_PIPE = $savedPipe
}
