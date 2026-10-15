# Changelog

All notable changes to this project are documented here. Versions follow
[Semantic Versioning](https://semver.org/).

## [0.1.0] - 2026-10-15

First release.

### Added

- `panetag.exe`: runs a shell or command in a pseudo console one row shorter than the
  pane and keeps a title bar on the reserved row, including under full-screen apps.
- Top (default) or bottom bar position. A bottom bar keeps Windows Terminal scrollback.
- Bar shows your title on the left and the running app's window title on the right.
  The tab title becomes `Your title · app title`, and the original is restored on exit.
- Named colors, `#RRGGBB` colors, automatic text contrast, and a stable automatic color per title.
- Rename and recolor a running bar over a named pipe (`PANETAG_PIPE`).
- Resizes follow the pane; mouse and cursor-position reports are adjusted for a top bar.
- `PaneTag` PowerShell module with `Set-PaneTag` (alias `ptag`), for Windows
  PowerShell 5.1 and PowerShell 7.
- `build.ps1` (no SDK needed), `install.ps1`, and headless smoke tests in `tests/`.
