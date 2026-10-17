# Plays the README demo in a full-screen, 4-pane Windows Terminal window, for you to record
# with your own screen recorder:
#
#   1. Run .\tools\demo\play.ps1 [-Seconds 45]
#   2. Start recording during the countdown.
#   3. Stop recording when the demo window closes by itself.
#
# Panes: Frontend and Backend run real Claude Code sessions (two small prompts) with a
# temporary settings file that only holds PaneTag's hooks, in the repo folder with
# --permission-mode default. Frontend finishes and turns orange ("Your turn"); Backend asks
# permission to create a file and turns red ("Needs you"). The request is never approved.
# Tests renames/recolors its bar with typed commands; Logs has a bottom bar and a status.
#
# The window closes by itself: the scenes exit when a stop file appears and the demo's
# Claude sessions end, so every pane exits normally. Don't close it from outside.
param([int]$Seconds = 45, [int]$Countdown = 5)
$ErrorActionPreference = 'Stop'

$root = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$work = Join-Path $env:TEMP 'panetag-demo'
if (Test-Path $work) { Remove-Item -Recurse -Force $work }
New-Item -ItemType Directory -Force $work | Out-Null
Import-Module (Join-Path $root 'PaneTag\PaneTag.psd1') -Force
Enable-PaneTagClaudeHooks -SettingsPath (Join-Path $work 'settings.json') | Out-Null

$exe = Join-Path $root 'PaneTag\panetag.exe'
$scene = Join-Path $PSScriptRoot 'scene.ps1'
function Pane([string]$Name, [string[]]$Bar) {
    @('-d', $root, $exe) + $Bar +
        @('--', 'powershell', '-NoLogo', '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $scene, $Name, $work)
}
$wtArgs = @('-w', 'new', '--fullscreen', 'nt') + (Pane 'frontend' @('Frontend', '-c', 'blue')) +
          @(';', 'sp', '-V') + (Pane 'backend' @('Backend', '-c', 'green')) +
          @(';', 'mf', 'left', ';', 'sp', '-H') + (Pane 'tests' @('Tests', '-c', 'teal')) +
          @(';', 'mf', 'right', ';', 'sp', '-H') + (Pane 'logs' @('Logs', '-c', 'gray', '-p', 'bottom'))

Write-Host 'Start your screen recorder now.' -ForegroundColor Yellow
for ($i = $Countdown; $i -gt 0; $i--) { Write-Host "  $i..."; Start-Sleep -Seconds 1 }
# If this runs inside a titled pane, don't let the demo panes inherit its pipe (they'd
# update this pane's bar instead of starting their own). Restored right after.
$savedPipe = $env:PANETAG_PIPE
$env:PANETAG_PIPE = $null
try { & wt.exe @wtArgs } finally { $env:PANETAG_PIPE = $savedPipe }
Write-Host "Demo running for $Seconds s. Don't touch the demo window; stop recording when it closes."

try {
    Start-Sleep -Seconds $Seconds
}
finally {
    New-Item -ItemType File -Force (Join-Path $work 'stop') | Out-Null
    # Only the demo's Claude sessions: their command line holds the temp settings path.
    Get-CimInstance Win32_Process -Filter "Name='claude.exe'" |
        Where-Object { $_.CommandLine -like '*panetag-demo*' } |
        ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
    Start-Sleep -Seconds 3
    Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue
}
Write-Host 'Done. Stop your recording.' -ForegroundColor Green
