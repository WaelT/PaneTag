# Builds, tests and publishes the PaneTag module to the PowerShell Gallery.
#
#   .\tools\publish.ps1            # asks for the API key (input hidden)
#   .\tools\publish.ps1 -WhatIf    # everything except the upload
#
# The key can also come from $env:PSGALLERY_API_KEY. Run this in your own terminal so the
# key never leaves it. Create a key at https://www.powershellgallery.com/account/apikeys
# (scope: "Push new packages and package versions", glob pattern: PaneTag).
#
# Uses PSResourceGet rather than PowerShellGet 2.x: PowerShellGet packs with `dotnet pack`
# whenever dotnet.exe is on PATH, which fails on machines with the .NET runtime but no SDK.
[CmdletBinding(SupportsShouldProcess = $true)]
param(
    # Publish somewhere else, e.g. a local folder repository for a dry run.
    [string]$Repository = 'PSGallery',
    [switch]$SkipTests
)
$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12

$root = Split-Path -Parent $PSScriptRoot

# 1. Publishing tool.
if (-not (Get-Module -ListAvailable Microsoft.PowerShell.PSResourceGet)) {
    Write-Host 'Installing Microsoft.PowerShell.PSResourceGet for your user...'
    Install-Module Microsoft.PowerShell.PSResourceGet -Scope CurrentUser -Force -AllowClobber
}
Import-Module Microsoft.PowerShell.PSResourceGet

# 2. Build and test.
if (-not $SkipTests) {
    & (Join-Path $root 'tests\smoke.ps1')
    if ($LASTEXITCODE -ne 0) { throw 'Smoke tests failed; not publishing.' }
}
else {
    & (Join-Path $root 'build.ps1')
}

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

$existing = Find-PSResource -Name PaneTag -Version "$version" -Repository $Repository -ErrorAction SilentlyContinue
if ($existing) { throw "PaneTag $version is already in $Repository. Bump ModuleVersion in PaneTag.psd1." }

# 4. Publish.
if ($PSCmdlet.ShouldProcess("PaneTag $version", "Publish to $Repository")) {
    $publish = @{ Path = $stage; Repository = $Repository }
    if ($Repository -eq 'PSGallery') {
        $key = $env:PSGALLERY_API_KEY
        if (-not $key) {
            $secure = Read-Host 'PowerShell Gallery API key' -AsSecureString
            $key = [Runtime.InteropServices.Marshal]::PtrToStringAuto([Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure))
        }
        $publish.ApiKey = $key
    }
    Publish-PSResource @publish
    Write-Host "Published PaneTag $version to $Repository." -ForegroundColor Green
    if ($Repository -eq 'PSGallery') {
        Write-Host "It can take a few minutes to appear: https://www.powershellgallery.com/packages/PaneTag/$version"
    }
}
