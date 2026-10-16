# Builds, tests and publishes the PaneTag module to the PowerShell Gallery.
#
#   .\tools\publish.ps1            # asks for the API key (input hidden)
#   .\tools\publish.ps1 -WhatIf    # everything except the upload
#
# The key can also come from $env:PSGALLERY_API_KEY. Run this in your own terminal so the
# key never leaves it. Create a key at https://www.powershellgallery.com/account/apikeys
# (scope: "Push new packages and package versions", glob pattern: PaneTag).
[CmdletBinding(SupportsShouldProcess = $true)]
param()
$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12

$root = Split-Path -Parent $PSScriptRoot

# 1. Publishing tools. Windows PowerShell ships PowerShellGet 1.0, which can't publish
#    reliably; 2.2.5 needs a fresh session after installing.
$psget = Get-Module -ListAvailable PowerShellGet | Sort-Object Version -Descending | Select-Object -First 1
if ($psget.Version -lt [version]'2.2.5') {
    Write-Host 'Installing the NuGet provider and PowerShellGet 2.2.5 for your user...'
    Install-PackageProvider -Name NuGet -MinimumVersion 2.8.5.201 -Scope CurrentUser -Force | Out-Null
    Install-Module PowerShellGet -MinimumVersion 2.2.5 -Scope CurrentUser -Force -AllowClobber
    Write-Host 'Done. Open a NEW PowerShell window and run this script again.' -ForegroundColor Yellow
    return
}
Import-Module PowerShellGet -MinimumVersion 2.2.5

# 2. Build and test.
& (Join-Path $root 'tests\smoke.ps1')
if ($LASTEXITCODE -ne 0) { throw 'Smoke tests failed; not publishing.' }

# 3. Stage only the module files (the folder name becomes the package name).
$manifest = Test-ModuleManifest (Join-Path $root 'PaneTag\PaneTag.psd1')
$version = $manifest.Version
$stage = Join-Path $env:TEMP 'panetag-publish\PaneTag'
if (Test-Path $stage) { Remove-Item -Recurse -Force $stage }
New-Item -ItemType Directory -Force $stage | Out-Null
foreach ($file in 'PaneTag.psd1', 'PaneTag.psm1', 'panetag.exe', 'ptag.exe') {
    Copy-Item (Join-Path $root "PaneTag\$file") $stage
}
Copy-Item (Join-Path $root 'LICENSE') $stage

$existing = Find-Module PaneTag -RequiredVersion $version -Repository PSGallery -ErrorAction SilentlyContinue
if ($existing) { throw "PaneTag $version is already on the Gallery. Bump ModuleVersion in PaneTag.psd1." }

# 4. Publish.
if ($PSCmdlet.ShouldProcess("PaneTag $version", 'Publish to the PowerShell Gallery')) {
    $key = $env:PSGALLERY_API_KEY
    if (-not $key) {
        $secure = Read-Host 'PowerShell Gallery API key' -AsSecureString
        $key = [Runtime.InteropServices.Marshal]::PtrToStringAuto([Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure))
    }
    Publish-Module -Path $stage -NuGetApiKey $key -Repository PSGallery
    Write-Host "Published PaneTag $version. It can take a few minutes to appear:" -ForegroundColor Green
    Write-Host "  https://www.powershellgallery.com/packages/PaneTag/$version"
}
