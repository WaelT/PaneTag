$script:Exe = Join-Path $PSScriptRoot 'panetag.exe'

function ConvertTo-B64([string]$Text) {
    [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($Text))
}

function Send-PaneTagCommand([string[]]$Lines) {
    $client = New-Object System.IO.Pipes.NamedPipeClientStream('.', $env:PANETAG_PIPE, [System.IO.Pipes.PipeDirection]::Out)
    try {
        $client.Connect(2000)
        $writer = New-Object System.IO.StreamWriter($client, (New-Object System.Text.UTF8Encoding($false)))
        foreach ($line in $Lines) { $writer.WriteLine($line) }
        $writer.Flush()
    }
    finally {
        $client.Dispose()
    }
}

<#
.SYNOPSIS
Puts a persistent title bar on the current terminal pane, or renames the existing one.

.DESCRIPTION
Outside a titled pane, starts a new shell (or -Command) under panetag.exe, which reserves
one row for the bar so full-screen apps such as claude or codex cannot draw over it.
Type 'exit' to remove the bar again.

Inside a titled pane, updates the bar in place.

.EXAMPLE
ptag 'API server'

.EXAMPLE
ptag 'Agent 1' -Color purple -Command claude

.EXAMPLE
ptag 'Long session' -Position Bottom   # keeps Windows Terminal scrollback
#>
function Set-PaneTag {
    [CmdletBinding()]
    param(
        [Parameter(Position = 0)]
        [string]$Title,

        # Named color (blue, green, purple, orange, red, teal, pink, yellow, indigo, cyan, brown, gray, black, white) or #RRGGBB.
        [string]$Color,

        # Text color; defaults to black or white for contrast.
        [string]$Foreground,

        # Top (default) or Bottom. Bottom keeps native scrollback in Windows Terminal.
        [ValidateSet('Top', 'Bottom')]
        [string]$Position,

        # Command line to run instead of a new shell; the pane returns to this shell when it exits.
        [string]$Command
    )

    if ($env:PANETAG_PIPE) {
        if ($Position -or $Command) {
            Write-Warning 'This pane already has a title bar; -Position and -Command only apply when starting one. Type exit to remove it first.'
        }
        $lines = @()
        if ($PSBoundParameters.ContainsKey('Title')) { $lines += "title $Title" }
        if ($Color) { $lines += "color $Color" }
        if ($Foreground) { $lines += "fg $Foreground" }
        if ($lines.Count -eq 0) { return }
        try {
            Send-PaneTagCommand $lines
        }
        catch {
            Write-Error "Cannot reach this pane's title bar ($env:PANETAG_PIPE): $($_.Exception.Message)"
        }
        return
    }

    if (-not (Test-Path $script:Exe)) {
        throw "panetag.exe not found next to the module ($script:Exe). Run build.ps1."
    }

    $cwd = (Get-Location -PSProvider FileSystem).ProviderPath
    if (-not $Title) { $Title = Split-Path -Leaf $cwd }
    if (-not $Command) { $Command = '"' + (Get-Process -Id $PID).Path + '" -NoLogo' }

    # Base64 sidesteps Windows PowerShell's mangling of quotes in native arguments.
    $arguments = @('--title-b64', (ConvertTo-B64 $Title), '--cmd-b64', (ConvertTo-B64 $Command), '--cwd-b64', (ConvertTo-B64 $cwd))
    if ($Color) { $arguments += @('--color', $Color) }
    if ($Foreground) { $arguments += @('--fg', $Foreground) }
    if ($Position) { $arguments += @('--position', $Position.ToLowerInvariant()) }

    & $script:Exe @arguments
}

Set-Alias -Name ptag -Value Set-PaneTag

Export-ModuleMember -Function Set-PaneTag -Alias ptag
