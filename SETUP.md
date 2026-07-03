# Setup Notes

Date: 2026-07-03

## Found

- Windows PowerShell workspace. Use a placeholder path such as `D:\Projects\TokenArtTagger` when documenting local setup publicly.
- .NET runtimes installed globally:
  - `Microsoft.NETCore.App 6.0.36`
  - `Microsoft.NETCore.App 10.0.1`
  - `Microsoft.WindowsDesktop.App 6.0.36`
  - `Microsoft.WindowsDesktop.App 10.0.1`
- Git was already installed outside the repository.
  - Verified with `git --version` using the existing Git executable.
  - Version: `2.43.0.windows.1`
  - Git may not be on PATH in every PowerShell session.

## Installed

- .NET SDK `10.0.301`
  - Installed project-locally at `.tools\dotnet`
  - Installed with Microsoft `dotnet-install.ps1`
  - No admin/global SDK install was used.

## Project-Local Build State

- NuGet package cache: `.nuget\packages`
- Dotnet CLI home used during Codex builds: `.dotnet-home`
- Redirected AppData used during Codex builds:
  - `.appdata\roaming`
  - `.appdata\local`

These local state folders are ignored by `.gitignore`.

## Skipped

- Git install was skipped because Git already exists on this machine.
- SQLite was skipped because v0.1 is intentionally in-memory/file-based.
- AI tagging dependencies were skipped because v0.1 does not include AI tagging.
