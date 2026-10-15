# Builds panetag.exe and installs the PaneTag module for Windows PowerShell and
# PowerShell 7, so `ptag` / `Set-PaneTag` auto-load in every new session.
$ErrorActionPreference = 'Stop'

& (Join-Path $PSScriptRoot 'build.ps1')

$docs = [Environment]::GetFolderPath('MyDocuments')
foreach ($root in @('WindowsPowerShell', 'PowerShell')) {
    $target = Join-Path $docs "$root\Modules\PaneTag"
    New-Item -ItemType Directory -Force $target | Out-Null
    Copy-Item (Join-Path $PSScriptRoot 'PaneTag\*') $target -Force
    Write-Host "Installed to $target"
}
Write-Host "Open a new pane and run: ptag 'My title'"
