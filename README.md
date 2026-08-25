<div align="center">

# PolarisTools

**Polaris 系列的 Visual Studio 扩展 / Visual Studio extension for the Polaris mod series**

[![License: LGPL v2.1](https://img.shields.io/badge/license-LGPL%202.1-blue.svg)](LICENSE.txt)
[![VSIX](https://img.shields.io/badge/Visual%20Studio-2022%2B-5c2d91.svg)](https://visualstudio.microsoft.com/)
[![Status](https://img.shields.io/badge/status-WIP-orange.svg)]()

[English](#english) · [中文](#中文)

</div>

---

## 中文

### 简介

PolarisTools 是 **Polaris** 系列的配套开发工具：一个 Visual Studio 扩展，为系列自有文件格式提供可视化编辑与代码生成。

### 特性

| 文件 | 编辑器 | 生成 |
| --- | --- | --- |
| `.pui` | 可视化 UI 编辑器，带实时预览、颜色/数值/列表专用控件 | `.g.cs` 强类型 UI 构建代码 + 交互逻辑的 code-behind 骨架 |
| `.puisln` | 节点图编辑器，连线定义 PUI 之间的跳转 | `.psg.cs` 不可变图蓝图 |
| `.plang` | 表格式多语言本地化编辑器：语言从游戏自带语言里点选、可单独启用/禁用，文案一律是可换行文本，支持 CSV 导入导出 | 强类型 Key 属性 + 编译期自动注册类（保存即生成，没有单独的生成按钮） |
| `.peffect` | 原版粒子语法编辑器，带 **Debug in game** 真机推送 | 自动设为 EmbeddedResource，由 PolarisParticles 启动时注册 |
| `.pmap` | CP/PIC/LP/GRD/SM/JOINT 完整地图蓝图编辑器；可按需临时导出本机 MapChips 到 PixelLiner 预览 | `.g.cs` 地图载入入口；XML 由 PolarisMap 编译为 TMAP v4 |
| `.pitem` | 物品聚合根编辑器：身份、库存、文案、图标和 Behavior | `.pitem.g.cs` 物品定义提供器与稳定 Id |
| `.pplugin` | 游戏内插件切面编辑器，通过 `ItemId` 关联 `.pitem` | `.pplugin.g.cs` Enhancer Facet 注册代码 |
| `.pskill` | 技能切面编辑器：所属物品、模式、解锁策略和 Behavior | `.pskill.g.cs` Skill Facet 注册代码 |

- **热重载** —— 编辑 `.pui` 时把改动直接推送给运行中的游戏，不用重启（需要目标程序集标 `[PUIHotFixEnabled]`）
- **粒子真机调试** —— 打开 `.peffect` 后点击 **Debug in game**，把项目内特效推送到运行中的游戏；F9 打开 IMGUI 预览页（需要目标插件类标 `[PEffectDebugEnabled]`）
- **地图整图热重载** —— `.pmap` 的 **Full hot reload** 推送完整 XML，游戏彻底关闭并重新加载地图；F11 打开 PolarisMap 独立 IMGUI 检查台（需要目标插件类标 `[PMapHotFixEnabled]`）
- **原版素材私有预览** —— **Preview originals** 让运行中的游戏把用户本机 MapChips Bundle 临时导出到系统临时目录并用 PixelLiner 打开；**Clear preview** 删除缓存，素材不会进入工程或 VSIX
- **项目模板** —— "添加新建项"里可直接创建 `.pui` / `.puisln` / `.plang` / `.peffect` / `.pitem` / `.pplugin` / `.pskill`
- **`.plang` 不再依赖运行时数据文件** —— 保存时生成的代码会在程序集加载时把 Key/多语言文案直接注册进 Polaris 的本地化运行时，不需要把 `.plang` 文件本身塞进发布包；项目只要引用 `Polaris.dll` 就够了

### 构建

VSIX 项目**不能用 `dotnet build`**，要用 MSBuild：

```powershell
& "<VS 安装目录>\MSBuild\Current\Bin\MSBuild.exe" PolarisTools.csproj /t:Build /p:Configuration=Debug
```

本仓库通过源码链接复用 Polaris 运行时库的实现（`.plang` 内存模型、PUI 热重载线协议、
`&键` 本地化写法的判定），因此**要求 Polaris 与本仓库并排 clone 在同一个父目录下**；
如果 Polaris 不在兄弟目录，可以在 `Directory.Build.props.user` 里覆盖 `PolarisDir` 属性指定位置
（不再支持环境变量）。

### 相关项目

| 项目 | 说明 |
| --- | --- |
| [Polaris](https://github.com/AAAA9731/Polaris) | 运行时库：本工具编辑的三种文件格式全在那边落地，部分源文件由本仓库链接复用 |

### 许可证

本项目基于 [LGPL-2.1](LICENSE.txt) 许可证开源。

感谢 Claude Code 节省的大量时间。

---

## English

### Overview

PolarisTools is a companion development tool for the **Polaris** mod series: a Visual Studio
extension providing visual editing and code generation for the series' custom file formats.
It has no runtime component of its own — the actual runtime logic for `.pui`, `.puisln` and
`.plang` lives in [Polaris](https://github.com/AAAA9731/Polaris), and this repo reuses part of
that implementation via source links, including the shared `.pmap` XML model and hot-reload protocol.

### Features

| File | Editor | Generates |
| --- | --- | --- |
| `.pui` | Visual UI editor with live preview and dedicated colour/number/list controls | `.g.cs` build code + code-behind skeleton |
| `.puisln` | Node-graph editor wiring transitions between PUIs | `.psg.cs` immutable graph blueprint |
| `.plang` | Grid-style multi-language localization editor: pick languages from the ones the game ships, enable/disable each, every value is wrappable text, CSV import/export | Strongly-typed key properties + a compile-time auto-registration class (generated on save — there is no separate generate button) |
| `.pmap` | Full CP/PIC/LP/GRD/SM/JOINT blueprint editor, with opt-in temporary PixelLiner preview of locally owned MapChips | `.g.cs` load entry; PolarisMap compiles the XML wrapper to TMAP v4 |
| `.peffect` | Original particle-syntax editor with **Debug in game** live push | Automatically marked EmbeddedResource and registered by PolarisParticles at startup |
| `.pitem` | Item aggregate editor for identity, inventory, presentation, and Behavior | `.pitem.g.cs` item provider and stable Id |
| `.pplugin` | Enhancer-facet editor linked to a `.pitem` through `ItemId` | `.pplugin.g.cs` plugin facet registration |
| `.pskill` | Skill-facet editor for owner item, mode, unlock policy, and Behavior | `.pskill.g.cs` skill facet registration |

- **Hot reload** — push `.pui` edits straight into the running game (target assembly must be tagged `[PUIHotFixEnabled]`)
- **In-game particle debugging** — open a `.peffect`, click **Debug in game**, then press F9 in the running game to inspect and play it (target plugin class must be tagged `[PEffectDebugEnabled]`)
- **Item templates** — create `.pui` / `.puisln` / `.plang` / `.peffect` / `.pitem` / `.pplugin` / `.pskill` from "Add New Item"
- **`.plang` no longer needs a runtime data file** — the code generated on save registers its keys/multi-language text into Polaris's localization runtime when the assembly loads, so `.plang` files themselves don't need to ship in the release package; a reference to `Polaris.dll` is all a project needs

### Building

VSIX projects **cannot be built with `dotnet build`** — use MSBuild:

```powershell
& "<VS install>\MSBuild\Current\Bin\MSBuild.exe" PolarisTools.csproj /t:Build /p:Configuration=Debug
```

This repo links source from the Polaris runtime library (the `.plang` in-memory model, the PUI
wire protocol, and the `&key` localization rule), so **Polaris and this repo must be cloned side
by side under one parent directory** — if it lives elsewhere, override the `PolarisDir` property in
`Directory.Build.props.user` (no environment variable support anymore).

### Related Projects

| Project | Description |
| --- | --- |
| [Polaris](https://github.com/AAAA9731/Polaris) | The runtime library — all three file formats land there; some of its source files are linked into this repo |

### License

Released under the [LGPL-2.1](LICENSE.txt) license.

Thanks to Claude Code for a bunch of times saved.
