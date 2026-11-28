# HNAUnlocker

[![GitHub Release](https://img.shields.io/github/v/release/Schrodingers-Neko/HNAUnlocker?color=3b82f6&logo=github)](https://github.com/Schrodingers-Neko/HNAUnlocker/releases/latest)
[![CI](https://github.com/Schrodingers-Neko/HNAUnlocker/actions/workflows/build.yml/badge.svg)](https://github.com/Schrodingers-Neko/HNAUnlocker/actions/workflows/build.yml)
[![License: GPL-3.0](https://img.shields.io/badge/License-GPL--3.0-10b981.svg)](LICENSE)
[![Platform](https://img.shields.io/badge/Platform-Windows-0078D6?logo=windows)](https://github.com/Schrodingers-Neko/HNAUnlocker/releases/latest)

简体中文 · [English](README.en.md)

**崩坏：因缘精灵（Honkai: Nexus Anima）** 帧率解锁工具，基于 [L1angYun/HSRUnlocker](https://github.com/L1angYun/HSRUnlocker)（GPL-3.0）改编移植。支持 10–480 FPS 上限、FOV 调整与角色抗淡出（AntiFade）。

## 下载与启动

从[发布页](https://github.com/Schrodingers-Neko/HNAUnlocker/releases)下载最新安装包 `HNAUnlocker-版本号-win-x64-Setup.exe`（可对照 `SHA256SUMS.txt` 校验 SHA-256）。需要 Windows 10 22H2（19045）或更新版本（x64）与管理员权限，缺少 .NET 8 或 Windows App Runtime 组件时安装程序会从微软下载。

1. 运行安装程序并使用快捷方式打开工具；首次启动会自动扫描 `NexusAnima.exe`（仅一个时自动保存，多个时供选择，未找到时可手动浏览）。
2. 设置目标帧率与选项后，点击「启动游戏」，或点击「连接游戏」附加到已运行的客户端。
3. 点击「停止并还原」结束当前会话并还原内存修改，游戏继续运行；最小化后可双击托盘图标恢复窗口。

## 功能说明

- **帧率与镜头**：支持 10–480 FPS 上限、FOV 偏移以及低仰角/近距离角色抗淡出（AntiFade），通过结构解析动态定位目标，不依赖固定版本偏移。
- **移动端 UI（暂禁）**：相关启动代码已保留，因当前 PC 测试版（`0.75.x`）缺少 `CloudGame` / `CloudInput` 模块，界面开关暂时禁用。
- **命令行与桌面快捷方式**：可用参数启动工具，例如 `HNAUnlocker.exe --launch --fps 144 --anti-fade on`；参数仅对本次运行有效，不会改动已保存的设置，`--help` 可查看全部选项，`--` 之后的内容会在工具启动游戏时传给 `NexusAnima.exe`。工具已在运行时，参数会交给已打开的窗口。主页的「创建桌面快捷方式」会把当前帧率、FOV 与抗淡出保存为快捷方式，已有相同快捷方式时不会重复创建；每次通过快捷方式启动都需要管理员确认。
- **自动更新与诊断**：支持通过 GitHub Releases 检查和校验更新；会话异常时可在「运行日志」或提示中导出不含路径与进程号的脱敏诊断 TXT，并提交至 [GitHub Issues](https://github.com/Schrodingers-Neko/HNAUnlocker/issues)（安全问题请参阅[安全说明](SECURITY.md)）。

## 开发与构建

```powershell
dotnet build .\gui\HNAUnlocker.csproj -c Release -r win-x64
dotnet run   --project .\tests\HNAUnlocker.Tests\HNAUnlocker.Tests.csproj -c Release
```

完整构建、PowerShell/Inno Setup 测试与打包流程见[参与开发](CONTRIBUTING.md)，底层实现说明见 [docs/HNA-Internals.md](docs/HNA-Internals.md)。

## 开源协议与致谢

本项目基于 [L1angYun/HSRUnlocker](https://github.com/L1angYun/HSRUnlocker) 改编，自研源码采用 [GPL-3.0-only](LICENSE) 开源；第三方组件许可见[第三方组件与许可](THIRD_PARTY_NOTICES.md)。本项目在开发中使用 AI 辅助，未经独立安全审计。

> 非官方工具：修改游戏进程可能违反游戏服务条款并带来账号风险，请自行评估后使用。
