$script:Exe = Join-Path $PSScriptRoot 'panetag.exe'
$script:HookExe = Join-Path $PSScriptRoot 'ptag.exe'

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
Puts a persistent title bar on the current terminal pane, or changes the existing one.

.DESCRIPTION
Outside a titled pane, starts a new shell (or -Command) under panetag.exe, which reserves
one row for the bar so full-screen apps such as claude or codex cannot draw over it.
Type 'exit' to remove the bar again.

Inside a titled pane, updates the bar in place. -Status flags the bar with a label and
color until the next keypress in the pane (or until -ClearStatus with -Sticky).

.EXAMPLE
ptag API server

.EXAMPLE
ptag 'Agent 1' -Color purple -Command claude

.EXAMPLE
ptag 'Long session' -Position Bottom   # keeps Windows Terminal scrollback

.EXAMPLE
ptag -Status 'Build failed' -StatusColor red -Sticky
#>
function Set-PaneTag {
    [CmdletBinding()]
    param(
        # Title; several words are joined, so quotes are optional.
        [Parameter(Position = 0, ValueFromRemainingArguments = $true)]
        [string[]]$Title,

        # Named color (blue, green, purple, orange, red, teal, pink, yellow, indigo, cyan, brown, gray, black, white) or #RRGGBB.
        [string]$Color,

        # Text color; defaults to black or white for contrast.
        [string]$Foreground,

        # Top (default) or Bottom. Bottom keeps native scrollback in Windows Terminal.
        [ValidateSet('Top', 'Bottom')]
        [string]$Position,

        # Command line to run instead of a new shell; the pane returns to this shell when it exits.
        [string]$Command,

        # Status label shown on the bar, which turns -StatusColor until the next keypress in the pane.
        [string]$Status,

        # Status color (default: orange).
        [string]$StatusColor = 'orange',

        # Keep the status until -ClearStatus instead of clearing it on the next keypress.
        [switch]$Sticky,

        # Remove the status.
        [switch]$ClearStatus
    )

    $titleText = $null
    if ($Title) { $titleText = $Title -join ' ' }
    $statusOnly = ($Status -or $ClearStatus) -and -not $titleText -and -not $Color -and -not $Foreground

    if ($env:PANETAG_PIPE) {
        if ($Position -or $Command) {
            Write-Warning 'This pane already has a title bar; -Position and -Command only apply when starting one. Type exit to remove it first.'
        }
        $lines = @()
        if ($titleText) { $lines += "title $titleText" }
        if ($Color) { $lines += "color $Color" }
        if ($Foreground) { $lines += "fg $Foreground" }
        if ($Status) {
            $lines += "statuscolor $StatusColor"
            if ($Sticky) { $lines += 'sticky 1' } else { $lines += 'sticky 0' }
            $lines += "status $Status"
        }
        elseif ($ClearStatus) { $lines += 'status ' }
        if ($lines.Count -eq 0) { return }
        try {
            Send-PaneTagCommand $lines
        }
        catch {
            Write-Error "Cannot reach this pane's title bar ($env:PANETAG_PIPE): $($_.Exception.Message)"
        }
        return
    }

    # Like ptag.exe, status updates outside a titled pane do nothing.
    if ($statusOnly) { return }

    if (-not (Test-Path $script:Exe)) {
        throw "panetag.exe not found next to the module ($script:Exe). Run build.ps1."
    }

    $cwd = (Get-Location -PSProvider FileSystem).ProviderPath
    if (-not $titleText) { $titleText = Split-Path -Leaf $cwd }
    if (-not $Command) { $Command = '"' + (Get-Process -Id $PID).Path + '" -NoLogo' }

    # Base64 sidesteps Windows PowerShell's mangling of quotes in native arguments.
    $arguments = @('--title-b64', (ConvertTo-B64 $titleText), '--cmd-b64', (ConvertTo-B64 $Command), '--cwd-b64', (ConvertTo-B64 $cwd))
    if ($Color) { $arguments += @('--color', $Color) }
    if ($Foreground) { $arguments += @('--fg', $Foreground) }
    if ($Position) { $arguments += @('--position', $Position.ToLowerInvariant()) }

    & $script:Exe @arguments
}

# ---------------------------------------------------------------- Claude Code hooks

function Test-PaneTagHandler($Handler) {
    $Handler.command -and ([string]$Handler.command) -match '[\\/]ptag\.exe$'
}

# Removes PaneTag's handlers from every event, dropping groups and events left empty.
function Remove-PaneTagHandlers($Hooks) {
    # Filter: on an empty object Windows PowerShell yields a single $null name.
    foreach ($event in @($Hooks.PSObject.Properties.Name | Where-Object { $_ })) {
        $groups = @()
        foreach ($group in @($Hooks.$event)) {
            $kept = @(@($group.hooks) | Where-Object { -not (Test-PaneTagHandler $_) })
            if ($kept.Count -gt 0) {
                $group.hooks = $kept
                $groups += $group
            }
        }
        if ($groups.Count -gt 0) { $Hooks.$event = $groups }
        else { $Hooks.PSObject.Properties.Remove($event) }
    }
}

function Read-ClaudeSettings([string]$Path) {
    if ((Test-Path $Path) -and (Get-Content -Raw $Path).Trim()) {
        return Get-Content -Raw $Path | ConvertFrom-Json
    }
    return New-Object PSObject
}

function Save-ClaudeSettings($Settings, [string]$Path) {
    $dir = Split-Path -Parent $Path
    if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Force $dir | Out-Null }
    if (Test-Path $Path) {
        $stamp = "$Path.bak-" + (Get-Date -Format 'yyyyMMdd-HHmmss')
        $backup = $stamp
        for ($i = 2; Test-Path $backup; $i++) { $backup = "$stamp-$i" } # never overwrite an earlier backup
        Copy-Item $Path $backup
        Write-Host "Backed up $Path to $backup"
    }
    $json = $Settings | ConvertTo-Json -Depth 32
    [IO.File]::WriteAllText($Path, $json, (New-Object System.Text.UTF8Encoding($false)))
}

<#
.SYNOPSIS
Adds Claude Code hooks that flag the pane's bar when Claude needs you.

.DESCRIPTION
Adds three hooks to Claude Code's user settings (~/.claude/settings.json by default):
  Stop                           -> bar shows "Your turn" (orange)
  Notification (permission_prompt) -> bar shows "Needs you" (red)
  UserPromptSubmit               -> status cleared
The status also clears on your next keypress in the pane. The hooks run ptag.exe from this
module's folder directly (no shell) and in the background, and do nothing when Claude isn't
running in a titled pane. Existing settings are kept and backed up first. Running it again
replaces PaneTag's hooks; run it again after moving or upgrading the module.

.EXAMPLE
Enable-PaneTagClaudeHooks

.EXAMPLE
Enable-PaneTagClaudeHooks -SettingsPath .\.claude\settings.json   # one project only
#>
function Enable-PaneTagClaudeHooks {
    [CmdletBinding()]
    param(
        [string]$SettingsPath = (Join-Path $HOME '.claude\settings.json'),
        [string]$YourTurnColor = 'orange',
        [string]$NeedsYouColor = 'red'
    )

    if (-not (Test-Path $script:HookExe)) {
        throw "ptag.exe not found next to the module ($script:HookExe). Run build.ps1."
    }
    $settings = Read-ClaudeSettings $SettingsPath
    if (-not $settings.PSObject.Properties['hooks']) {
        $settings | Add-Member -NotePropertyName hooks -NotePropertyValue (New-Object PSObject)
    }
    Remove-PaneTagHandlers $settings.hooks

    function New-Handler([string[]]$HandlerArgs) {
        [pscustomobject]@{ type = 'command'; command = $script:HookExe; args = $HandlerArgs; async = $true }
    }
    $add = [ordered]@{
        Stop             = [pscustomobject]@{ hooks = @(New-Handler @('--status', 'Your turn', '--status-color', $YourTurnColor)) }
        Notification     = [pscustomobject]@{ matcher = 'permission_prompt'; hooks = @(New-Handler @('--status', 'Needs you', '--status-color', $NeedsYouColor)) }
        UserPromptSubmit = [pscustomobject]@{ hooks = @(New-Handler @('--clear-status')) }
    }
    foreach ($event in $add.Keys) {
        $groups = @()
        if ($settings.hooks.PSObject.Properties[$event]) { $groups = @($settings.hooks.$event) }
        $groups += $add[$event]
        $settings.hooks | Add-Member -NotePropertyName $event -NotePropertyValue $groups -Force
    }

    Save-ClaudeSettings $settings $SettingsPath
    Write-Host "PaneTag hooks enabled in $SettingsPath (Stop, Notification, UserPromptSubmit)."
}

<#
.SYNOPSIS
Removes the hooks added by Enable-PaneTagClaudeHooks.
#>
function Disable-PaneTagClaudeHooks {
    [CmdletBinding()]
    param(
        [string]$SettingsPath = (Join-Path $HOME '.claude\settings.json')
    )

    if (-not (Test-Path $SettingsPath)) { Write-Host "No settings file at $SettingsPath; nothing to do."; return }
    $settings = Read-ClaudeSettings $SettingsPath
    if (-not $settings.PSObject.Properties['hooks']) { Write-Host 'No hooks configured; nothing to do.'; return }
    Remove-PaneTagHandlers $settings.hooks
    if (@($settings.hooks.PSObject.Properties).Count -eq 0) { $settings.PSObject.Properties.Remove('hooks') }
    Save-ClaudeSettings $settings $SettingsPath
    Write-Host "PaneTag hooks removed from $SettingsPath."
}

Set-Alias -Name ptag -Value Set-PaneTag

Export-ModuleMember -Function Set-PaneTag, Enable-PaneTagClaudeHooks, Disable-PaneTagClaudeHooks -Alias ptag
