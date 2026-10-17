# One pane of the demo recording (see record.ps1). Runs inside panetag.exe.
param([Parameter(Mandatory = $true)][string]$Scene, [Parameter(Mandatory = $true)][string]$Work)
$ErrorActionPreference = 'Continue'
Import-Module (Join-Path $PSScriptRoot '..\..\PaneTag\PaneTag.psd1') -Force
$settings = Join-Path $Work 'settings.json'
$stop = Join-Path $Work 'stop'
$check = [char]0x2713

function Type-Command([string]$Text) {
    Write-Host -NoNewline "PS $PWD> "
    foreach ($c in $Text.ToCharArray()) {
        Write-Host -NoNewline -ForegroundColor Yellow $c
        Start-Sleep -Milliseconds 35
    }
    Start-Sleep -Milliseconds 350
    Write-Host
}

function Wait-Stop { while (-not (Test-Path $stop)) { Start-Sleep -Milliseconds 200 }; exit 0 }

Clear-Host
switch ($Scene) {
    'frontend' {
        Start-Sleep -Milliseconds 1500
        $prompt = 'Suggest a name for a dark-mode toggle setting. One line.'
        Type-Command "claude `"$prompt`""
        & claude --settings $settings --permission-mode default $prompt
        exit 0
    }
    'backend' {
        Start-Sleep -Milliseconds 3000
        $prompt = 'Create a file named todo.txt listing three backend tasks.'
        Type-Command "claude `"$prompt`""
        & claude --settings $settings --permission-mode default $prompt
        exit 0
    }
    'tests' {
        Start-Sleep -Milliseconds 5000
        Type-Command "ptag 'Tests (running)'"
        ptag 'Tests (running)'
        Start-Sleep -Milliseconds 600
        Type-Command 'npm test'
        $cases = 'auth > login', 'auth > logout', 'auth > refresh token', 'api > list users', 'api > create user',
                 'api > rate limit', 'ui > dark mode toggle', 'ui > settings page', 'ui > keyboard shortcuts'
        foreach ($case in $cases) {
            Write-Host -ForegroundColor Green -NoNewline "  $check "
            Write-Host "$case " -NoNewline
            Write-Host -ForegroundColor DarkGray ("(" + (Get-Random -Minimum 8 -Maximum 240) + " ms)")
            Start-Sleep -Milliseconds 700
        }
        Write-Host
        Write-Host -ForegroundColor Green 'Tests: 9 passed, 9 total'
        Start-Sleep -Milliseconds 800
        Type-Command 'ptag Tests: 9 passing -Color green'
        ptag Tests: 9 passing -Color green
        Wait-Stop
    }
    'logs' {
        $paths = '/api/users', '/api/orders', '/api/health', '/api/settings', '/login', '/api/search'
        $start = Get-Date
        $flagged = $false
        while (-not (Test-Path $stop)) {
            $elapsed = ((Get-Date) - $start).TotalSeconds
            $time = (Get-Date).ToString('HH:mm:ss')
            if ($elapsed -gt 20 -and $elapsed -lt 23) {
                Write-Host -ForegroundColor Red "[$time] ERROR  POST /api/orders 500 upstream timeout"
                if (-not $flagged -and $elapsed -gt 22) {
                    ptag -Status '3 errors' -StatusColor yellow -Sticky
                    $flagged = $true
                }
            }
            else {
                $path = $paths | Get-Random
                Write-Host -ForegroundColor DarkGray -NoNewline "[$time] "
                Write-Host "INFO   GET $path 200 $(Get-Random -Minimum 3 -Maximum 90)ms"
            }
            Start-Sleep -Milliseconds 450
        }
        exit 0
    }
}
