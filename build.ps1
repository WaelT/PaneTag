# Compiles src\PaneTag.cs into PaneTag\panetag.exe using the C# compiler that
# ships with .NET Framework 4.x (no SDK required).
$ErrorActionPreference = 'Stop'

$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { throw "C# compiler not found at $csc" }

$src = Join-Path $PSScriptRoot 'src\PaneTag.cs'
$out = Join-Path $PSScriptRoot 'PaneTag\panetag.exe'

& $csc /nologo /optimize+ /target:exe /platform:anycpu "/out:$out" /r:System.Core.dll $src
if ($LASTEXITCODE -ne 0) { throw "Build failed ($LASTEXITCODE)" }
Write-Host "Built $out"
