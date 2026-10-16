@{
    RootModule        = 'PaneTag.psm1'
    ModuleVersion     = '0.2.0'
    GUID              = '6d3f2b0e-6a51-4c8e-9a3e-2f4f0c7b9d11'
    Author            = 'waelt86'
    Description       = 'Persistent per-pane title bars for Windows Terminal that survive full-screen apps like claude and codex.'
    PowerShellVersion = '5.1'
    FunctionsToExport = @('Set-PaneTag')
    AliasesToExport   = @('ptag')
    CmdletsToExport   = @()
    VariablesToExport = @()
}
