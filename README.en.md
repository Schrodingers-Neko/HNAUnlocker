# HNAUnlocker

[![GitHub Release](https://img.shields.io/github/v/release/Schrodingers-Neko/HNAUnlocker?color=3b82f6&logo=github)](https://github.com/Schrodingers-Neko/HNAUnlocker/releases/latest)
[![CI](https://github.com/Schrodingers-Neko/HNAUnlocker/actions/workflows/build.yml/badge.svg)](https://github.com/Schrodingers-Neko/HNAUnlocker/actions/workflows/build.yml)
[![License: GPL-3.0](https://img.shields.io/badge/License-GPL--3.0-10b981.svg)](LICENSE)
[![Platform](https://img.shields.io/badge/Platform-Windows-0078D6?logo=windows)](https://github.com/Schrodingers-Neko/HNAUnlocker/releases/latest)

[简体中文](README.md) · English

An FPS unlocker for **Honkai: Nexus Anima** (`NexusAnima.exe`), adapted from [L1angYun/HSRUnlocker](https://github.com/L1angYun/HSRUnlocker) under GPL-3.0. Supports a 10–480 FPS cap, FOV adjustment, and character anti-fade.

## Download and launch

Download the latest `HNAUnlocker-version-win-x64-Setup.exe` from [GitHub Releases](https://github.com/Schrodingers-Neko/HNAUnlocker/releases) (verify SHA-256 against `SHA256SUMS.txt`). Requires Windows 10 22H2 (build 19045) or later (x64) and administrator privileges; Setup downloads missing .NET 8 and Windows App Runtime components from Microsoft.

1. Run the installer and open the app from its shortcut. On first launch it scans for `NexusAnima.exe` (auto-saves a single match, prompts if multiple are found, or lets you browse manually).
2. Configure your target FPS and options, then click **Launch game** or **Connect to game** to attach to a running client.
3. Click **Stop and restore** to end the session and restore patches while the game keeps running. Double-click the tray icon to restore a minimized window.

## Features

- **Frame rate & camera**: 10–480 FPS cap, FOV delta, and anti-fade (prevents character dithering at low camera angles and close distances), resolved dynamically without hardcoded version offsets.
- **Mobile UI (disabled)**: Launch support is retained for future game builds, with the UI toggle disabled while the `0.75.x` PC beta lacks `CloudGame` / `CloudInput`.
- **Updates & diagnostics**: Built-in update checks via GitHub Releases; export a sanitized TXT diagnostic report (without paths or PIDs) from **Activity log** or error notices when filing [GitHub Issues](https://github.com/Schrodingers-Neko/HNAUnlocker/issues) (see [SECURITY.md](SECURITY.md) for security reports).

## Development

```powershell
dotnet build .\gui\HNAUnlocker.csproj -c Release -r win-x64
dotnet run   --project .\tests\HNAUnlocker.Tests\HNAUnlocker.Tests.csproj -c Release
```

See [Contributing](CONTRIBUTING.md#english) for full build, PowerShell/Inno Setup test, and release steps, and [docs/HNA-Internals.md](docs/HNA-Internals.md) for technical internals.

## License & attribution

Derived from [L1angYun/HSRUnlocker](https://github.com/L1angYun/HSRUnlocker) and licensed under [GPL-3.0-only](LICENSE); see [Third-party notices](THIRD_PARTY_NOTICES.md) for dependencies. Developed with AI assistance and not independently security audited.

> Unofficial tool: modifying the game process may violate the game's terms of service and put your account at risk.
