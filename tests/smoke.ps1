# Headless smoke tests: runs panetag.exe inside a pseudo console (tests\PtyHarness.cs)
# and checks the rendered output. Builds panetag.exe first unless -NoBuild is given.
param([switch]$NoBuild)
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
if (-not $NoBuild) { & (Join-Path $root 'build.ps1') }

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
function Invoke-Pty([string]$CommandLine, [string]$Rename) {
    $harnessArgs = @('60', '10', '15000', $CommandLine)
    if ($Rename) { $harnessArgs += $Rename }
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
$out = Invoke-Pty "`"$exe`" -t Before --cmd-b64 $(B64 'powershell -NoProfile -Command "Start-Sleep 3"')" 'After Rename'
Check 'rename over pipe' (($out -match '<<<RENAME SENT>>>') -and ($out -match ' After Rename ')) $out

# 3. Module: launch with -Command, rename from inside, exit code reaches the outer shell.
$inner = "Import-Module '$module'; Set-PaneTag 'Renamed Inside' -Color orange; Start-Sleep -Milliseconds 800; exit 3"
$outer = "Import-Module '$module'; Set-PaneTag 'Module Test' -Color purple -Command 'powershell -NoProfile -EncodedCommand $(Enc $inner)'; 'outer exit=' + `$LASTEXITCODE"
$out = Invoke-Pty "powershell -NoProfile -EncodedCommand $(Enc $outer)"
Check 'module starts the bar' ($out -match ' Module Test ') $out
Check 'module renames from inside' ($out -match ' Renamed Inside ') $out
Check 'module returns exit code' ($out -match 'outer exit=3') $out

# 4. panetag --rename by bare name (the pane puts panetag on PATH), unquoted words, with a color.
$child = 'cmd /c "panetag --rename Via Cmd -c red & ping -n 3 127.0.0.1 >nul"'
$out = Invoke-Pty "`"$exe`" -t Before --cmd-b64 $(B64 $child)"
Check '--rename from cmd' (($out -match ' Via Cmd ') -and ($out -match '48;2;207;34;46m')) $out

$bash = Join-Path $env:ProgramFiles 'Git\bin\bash.exe'
if (Test-Path $bash) {
    $child = "`"$bash`" -c `"panetag --rename Via Bash; sleep 2`""
    $out = Invoke-Pty "`"$exe`" -t Before --cmd-b64 $(B64 $child)"
    Check '--rename from Git Bash (Claude Code ! commands)' ($out -match ' Via Bash ') $out
}
else { Write-Host 'SKIP  --rename from Git Bash (Git Bash not installed)' -ForegroundColor Yellow }

# 5. --rename outside a titled pane fails cleanly.
$savedPipe = $env:PANETAG_PIPE
Remove-Item Env:PANETAG_PIPE -ErrorAction SilentlyContinue
$ErrorActionPreference = 'Continue' # PS 5.1 turns native stderr into a terminating error under 'Stop'
$err = & $exe --rename Nope 2>&1 | Out-String
$ErrorActionPreference = 'Stop'
Check '--rename outside a pane exits 1' (($LASTEXITCODE -eq 1) -and ($err -match 'not inside a PaneTag pane')) $err
if ($savedPipe) { $env:PANETAG_PIPE = $savedPipe }

if ($failures -gt 0) { throw "$failures check(s) failed" }
Write-Host 'All smoke tests passed.' -ForegroundColor Green
exit 0
