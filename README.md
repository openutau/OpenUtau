
# OpenUtau MCP

此分支将原生 HTTP MCP 集成到 OpenUtau 桌面应用。MCP 服务与应用进程一同运行，面向本机 OpenUtau 实例和本地 MCP 客户端。

## 原生 HTTP MCP

实现位于 `OpenUtau.Core/AgentBridge/`，使用 Streamable HTTP、JSON-RPC 2.0 和 Bearer Token。服务仅绑定回环地址，默认端点为 `http://127.0.0.1:43102/mcp`。

### 启动与连接

1. 在 OpenUtau 菜单中打开 `MCP`。
2. 选择 `启动 MCP 服务`。
3. 选择 `复制 MCP 连接配置`，将剪贴板中的 JSON 粘贴到 MCP 客户端配置中。

复制出的配置形如以下示例。Token 由应用生成，示例中的值仅为占位符：

```json
{
  "mcpServers": {
    "openutau": {
      "url": "http://127.0.0.1:43102/mcp",
      "headers": {
        "Authorization": "Bearer <MCP_TOKEN>"
      }
    }
  }
}
```

服务提供 `openutau_read`、`openutau_plan`、`openutau_apply` 和 `openutau_diagnostics`。写入操作先通过 `openutau_plan` 创建短期计划，再由 `openutau_apply` 以 `planId` 确认执行。

MCP 菜单还提供状态查看、停止服务、复制 Token 与刷新 Token。Token 在应用重启后保持有效；选择 `刷新 MCP Token` 并确认后，客户端需要粘贴新的连接配置。Token 属于本机凭据，请避免记录到日志、版本库或共享渠道。

### 构建与验证

```bash
/workspace/.dotnet/dotnet build OpenUtau/OpenUtau.csproj -p:BaseOutputPath=/workspace/openutau-build/

/workspace/.dotnet/dotnet test OpenUtau.Test/OpenUtau.Test.csproj --filter "FullyQualifiedName~BridgeProtocolTest" -p:BaseOutputPath=/workspace/openutau-build/
```

### 音频理解工作流

面向 AI 辅助工程编辑的外部音频理解工作流位于 [`.monkeycode/docs/AUDIO_UNDERSTANDING_WORKFLOW.md`](.monkeycode/docs/AUDIO_UNDERSTANDING_WORKFLOW.md)。该设计稿涵盖用户校对歌词、音素强制对齐、连续音高提取、结构化音频证据和可审核的工程修改计划。

### 相关 Bridge 分支

`origin/Insert` 分支保留 stdio Bridge 协调器和文件 IPC 方案。此分支专注于应用内原生 HTTP MCP。

原版 OpenUtau 与 MCP 修改版适合采用独立安装目录和独立用户数据目录并行部署。发布脚本应明确配置安装器目录、应用标识与用户数据重定向。

## OpenUtau

OpenUtau is a free, open-source editor made for the UTAU community.

[![Build](https://img.shields.io/github/actions/workflow/status/stakira/OpenUtau/build.yml?style=for-the-badge)](https://github.com/stakira/OpenUtau/actions/workflows/build.yml)
[![Discord](https://img.shields.io/discord/551606189386104834?style=for-the-badge&label=discord&logo=discord&logoColor=ffffff&color=7389D8&labelColor=6A7EC2)](https://discord.gg/UfpMnqMmEM)
[![QQ Qroup](https://img.shields.io/badge/QQ-485658015-blue?style=for-the-badge)](https://qm.qq.com/cgi-bin/qm/qr?k=8EtEpehB1a-nfTNAnngTVqX3o9xoIxmT&jump_from=webapi)
[![Trello](https://img.shields.io/badge/trello-go-blue?style=for-the-badge&logo=trello)](https://trello.com/b/93ANoCIV/openutau)

## Getting started

[![Download](https://img.shields.io/static/v1?style=for-the-badge&logo=github&label=download&message=windows-x64&labelColor=FF347C&color=4ea6ea)](https://github.com/stakira/OpenUtau/releases/latest/download/OpenUtau-win-x64.zip)</br>
[![Download](https://img.shields.io/static/v1?style=for-the-badge&logo=github&label=download&message=windows-x86&labelColor=FF347C&color=4ea6ea)](https://github.com/stakira/OpenUtau/releases/latest/download/OpenUtau-win-x86.zip)</br>
[![Download](https://img.shields.io/static/v1?style=for-the-badge&logo=github&label=download&message=macos-x64&labelColor=FF347C&color=4ea6ea)](https://github.com/stakira/OpenUtau/releases/latest/download/OpenUtau-osx-x64.dmg)</br>
[![Download](https://img.shields.io/static/v1?style=for-the-badge&logo=github&label=download&message=linux-x64&labelColor=FF347C&color=4ea6ea)](https://github.com/stakira/OpenUtau/releases/latest/download/OpenUtau-linux-x64.zip)

It is **strongly recommended** that you read these Github wiki pages before using the software.
- [Getting-Started](https://github.com/stakira/OpenUtau/wiki/Getting-Started)
- [Resamplers](https://github.com/stakira/OpenUtau/wiki/Resamplers-and-Wavtools)
- [Phonemizers](https://github.com/stakira/OpenUtau/wiki/Phonemizers)
- [FAQ](https://github.com/stakira/OpenUtau/wiki/FAQ)

- [中文使用说明](https://opensynth.miraheze.org/wiki/OpenUTAU/%E4%BD%BF%E7%94%A8%E6%96%B9%E6%B3%95)

## How to contribute

Tried OpenUtau and not satisfied? Don't just walk away! You can help:
- Report issues on our [Discord server](https://discord.gg/UfpMnqMmEM) or Github.
- Suggest features on Discord or Github.
- Add or update translations for your language on [Crowdin](https://crowdin.com/project/oxygen-dioxideopenutau).

Know how to code? Got an idea for an improvement? Don't keep it to yourself!
- Contribute fixes via pull requests.
- Check out the development roadmap on [Trello](https://trello.com/b/93ANoCIV/openutau) and discuss it on Discord.

## Plugin development

Want to contribute plugins to help other users? Check out our API documentation:
- [Editing Macros API Document](OpenUtau.Core/Editing/README.md)
- [Phonemizers API Document](OpenUtau.Core/Api/README.md)

## Main features

Navigate the interface naturally and fluently using the mouse and scroll wheel. Keyboard shortcuts are also available.

![Editor](Misc/GIFs/editor.gif)

Easily create songs and covers using the feature-rich MIDI editor.

![Editor](Misc/GIFs/editor2.gif)

Create expressive vibratos with the easy-to-use vibrato editor.

![Vibrato](Misc/GIFs/vibrato.gif)

Pre-rendering and built-in resamplers let you quickly preview your work.

![Playback](Misc/GIFs/playback.gif)

See the [Getting-Started Wiki page](https://github.com/stakira/OpenUtau/wiki/Getting-Started) for more!

## All features
- Modern user experience.
- Easy navigation using the mouse and keyboard.
- Feature-rich MIDI editor.
  - Support for importing VSQX (Vocaloid 4) tracks.
- Selective backward compatibility with UTAU.
  - OpenUtau aims to solve problems with fewer steps. It is not designed to replicate UTAU features exactly.
- Extensible real-time phonetic editing.
  - Includes phonemizers for different phonetic systems (VCV, CVVC, Arpasing, etc.) in many different languages (English, Japanese, Chinese, Korean, Russian and more).
- Expressions replace the standard UTAU "flags" for tuning.
  - The built-in WORLDLINE-R resampler supports curve tuning, similar to many vocal synth editors.
- Internationalisation, including UI translation and file system encoding support.
  - Unlike UTAU, there is no need to change your system locale to use OpenUtau.
- Smooth preview/rendering experience.
  - Pre-rendering allows OpenUtau to render vocals before playback, saving time during editing and tuning.
- Supports ENUNU AI singers. See the [ENUNU wiki page](https://github.com/stakira/OpenUtau/wiki/ENUNU-NNSVS-Support) for more info.
- Easy-to-use plugin system.
- Versatile resampling engine interface.
  - Compatible with most UTAU resamplers.
- Runs on Windows (32/64 bit), macOS, and Linux.

### What it doesn't do
- While OpenUtau can do very minimal mixing, it will not replace your digital audio workstation of choice.
- OpenUtau does not aim for Vocaloid compatibility, except for some limited features.
