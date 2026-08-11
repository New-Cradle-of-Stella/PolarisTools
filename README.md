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

PolarisTools 是 **Polaris** 系列的配套开发工具：一个 Visual Studio 扩展，为系列的三种自有文件格式提供可视化编辑与代码生成。

### 特性

| 文件 | 编辑器 | 生成 |
| --- | --- | --- |
| `.pui` | 可视化 UI 编辑器，带实时预览、颜色/数值/列表专用控件 | `.g.cs` 强类型 UI 构建代码 + 交互逻辑的 code-behind 骨架 |
| `.puisln` | 节点图编辑器，连线定义 PUI 之间的跳转 | `.psg.cs` 不可变图蓝图 |
| `.plang` | 表格式多语言本地化编辑器：语言从游戏自带语言里点选、可单独启用/禁用，文案一律是可换行文本，支持 CSV 导入导出 | 强类型 Key 属性 + 编译期自动注册类（保存即生成，没有单独的生成按钮） |

- **热重载** —— 编辑 `.pui` 时把改动直接推送给运行中的游戏，不用重启（需要目标程序集标 `[PUIHotFixEnabled]`）
- **项目模板** —— "添加新建项"里可直接创建 `.pui` / `.puisln` / `.plang`
- **`.plang` 不再依赖运行时数据文件** —— 保存时生成的代码会在程序集加载时把 Key/多语言文案直接注册进 Polaris 的本地化运行时，不需要把 `.plang` 文件本身塞进发布包；项目只要引用 `Polaris.dll` 就够了

### 构建

VSIX 项目**不能用 `dotnet build`**，要用 MSBuild：

```powershell
& "<VS 安装目录>\MSBuild\Current\Bin\MSBuild.exe" PolarisTools.csproj /t:Build /p:Configuration=Debug
```

本仓库通过源码链接复用 Polaris 运行时库的实现（`.plang` 内存模型、PUI 热重载线协议、
`&键` 本地化写法的判定），因此**要求 Polaris 与本仓库并排 clone 在同一个父目录下**；
也可以用 `POLARIS_DIR` 环境变量指定位置。

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
extension providing visual editing and code generation for the series' three custom file formats.
It has no runtime component of its own — the actual runtime logic for `.pui`, `.puisln` and
`.plang` lives in [Polaris](https://github.com/AAAA9731/Polaris), and this repo reuses part of
that implementation via source links.

### Features

| File | Editor | Generates |
| --- | --- | --- |
| `.pui` | Visual UI editor with live preview and dedicated colour/number/list controls | `.g.cs` build code + code-behind skeleton |
| `.puisln` | Node-graph editor wiring transitions between PUIs | `.psg.cs` immutable graph blueprint |
| `.plang` | Grid-style multi-language localization editor: pick languages from the ones the game ships, enable/disable each, every value is wrappable text, CSV import/export | Strongly-typed key properties + a compile-time auto-registration class (generated on save — there is no separate generate button) |

- **Hot reload** — push `.pui` edits straight into the running game (target assembly must be tagged `[PUIHotFixEnabled]`)
- **Item templates** — create `.pui` / `.puisln` / `.plang` from "Add New Item"
- **`.plang` no longer needs a runtime data file** — the code generated on save registers its keys/multi-language text into Polaris's localization runtime when the assembly loads, so `.plang` files themselves don't need to ship in the release package; a reference to `Polaris.dll` is all a project needs

### Building

VSIX projects **cannot be built with `dotnet build`** — use MSBuild:

```powershell
& "<VS install>\MSBuild\Current\Bin\MSBuild.exe" PolarisTools.csproj /t:Build /p:Configuration=Debug
```

This repo links source from the Polaris runtime library (the `.plang` in-memory model, the PUI
wire protocol, and the `&key` localization rule), so **Polaris and this repo must be cloned side
by side under one parent directory** — or point `POLARIS_DIR` at it.

### Related Projects

| Project | Description |
| --- | --- |
| [Polaris](https://github.com/AAAA9731/Polaris) | The runtime library — all three file formats land there; some of its source files are linked into this repo |

### License

Released under the [LGPL-2.1](LICENSE.txt) license.

Thanks to Claude Code for a bunch of times saved.
