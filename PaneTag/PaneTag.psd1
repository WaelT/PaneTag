@{
    RootModule           = 'PaneTag.psm1'
    ModuleVersion        = '0.3.0'
    GUID                 = '6d3f2b0e-6a51-4c8e-9a3e-2f4f0c7b9d11'
    Author               = 'waelt86'
    Copyright            = '(c) 2026 waelt86. MIT License.'
    Description          = 'Persistent per-pane title bars for Windows Terminal that survive full-screen apps like Claude Code and Codex. Rename from any shell with ptag, and let Claude Code hooks flag the pane that needs you.'
    PowerShellVersion    = '5.1'
    CompatiblePSEditions = @('Desktop', 'Core')
    FunctionsToExport    = @('Set-PaneTag', 'Enable-PaneTagClaudeHooks', 'Disable-PaneTagClaudeHooks')
    AliasesToExport      = @('ptag')
    CmdletsToExport      = @()
    VariablesToExport    = @()
    PrivateData          = @{
        PSData = @{
            Tags         = @('WindowsTerminal', 'Terminal', 'ConPTY', 'ClaudeCode', 'Claude', 'Codex', 'StatusBar', 'TitleBar', 'Pane', 'Windows', 'PSEdition_Desktop', 'PSEdition_Core')
            ProjectUri   = 'https://github.com/WaelT/PaneTag'
            LicenseUri   = 'https://github.com/WaelT/PaneTag/blob/main/LICENSE'
            ReleaseNotes = 'https://github.com/WaelT/PaneTag/blob/main/CHANGELOG.md'
        }
    }
}
