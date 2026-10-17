# Contributing

Thanks for helping! Bug reports, ideas and pull requests are all welcome.

## Reporting a bug

Open an issue with:

- your Windows version (`winver`) and Windows Terminal version
- how you installed PaneTag (PowerShell Gallery or from source) and its version
  (`(Get-Module -ListAvailable PaneTag).Version`)
- the exact command, the bar position, and the app you were running
- what you expected and what you saw (a screenshot helps)

[docs/troubleshooting.md](docs/troubleshooting.md) covers the common problems.

## Making a change

1. Read [docs/development.md](docs/development.md) (building, testing, C# 5 limits) and,
   for changes to `src/PaneTag.cs`, [docs/architecture.md](docs/architecture.md).
2. Build and test: `.\tests\smoke.ps1` must print `All smoke tests passed.` Add a smoke
   test for new behavior where you can. GitHub Actions runs the same tests on every push and
   pull request.
3. For anything visual, try the relevant
   [manual checks](docs/development.md#manual-checks-in-windows-terminal) in a real Windows
   Terminal and say in the pull request which ones you ran.
4. Keep the docs in sync: `panetag.exe --help`, [docs/usage.md](docs/usage.md), and an
   "Unreleased" entry in [CHANGELOG.md](CHANGELOG.md).

Coding agents: see [AGENTS.md](AGENTS.md).

## License of contributions

PaneTag is under the [MIT License](LICENSE). By submitting a contribution, you agree that it
is licensed under the same MIT License. You keep the copyright on your contribution; there is
no contributor license agreement and no transfer of ownership.
