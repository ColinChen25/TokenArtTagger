# Setup Notes

Date: 2026-07-02

## Found

- Windows PowerShell workspace: `D:\Documents\TokenArtTagger`
- .NET runtimes installed globally:
  - `Microsoft.NETCore.App 6.0.36`
  - `Microsoft.NETCore.App 10.0.1`
  - `Microsoft.WindowsDesktop.App 6.0.36`
  - `Microsoft.WindowsDesktop.App 10.0.1`
- Git installed at `F:\Git`
  - Verified with `F:\Git\cmd\git.exe --version`
  - Version: `2.43.0.windows.1`
  - Git is not on PATH in this PowerShell session.

## Installed

- .NET SDK `10.0.301`
  - Installed project-locally at `D:\Documents\TokenArtTagger\.tools\dotnet`
  - Installed with Microsoft `dotnet-install.ps1`
  - No admin/global SDK install was used.

## Project-Local Build State

- NuGet package cache: `D:\Documents\TokenArtTagger\.nuget\packages`
- Dotnet CLI home used during Codex builds: `D:\Documents\TokenArtTagger\.dotnet-home`
- Redirected AppData used during Codex builds:
  - `D:\Documents\TokenArtTagger\.appdata\roaming`
  - `D:\Documents\TokenArtTagger\.appdata\local`

These local state folders are ignored by `.gitignore`.

## Skipped

- Git install was skipped because Git already exists in `F:\Git`.
- SQLite was skipped because v0.1 is intentionally in-memory/file-based.
- AI tagging dependencies were skipped because v0.1 does not include AI tagging.
