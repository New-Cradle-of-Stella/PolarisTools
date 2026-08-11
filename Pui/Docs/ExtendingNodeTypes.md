# PUI 状态机节点类型说明

## 架构概述

`.puisln` 现在是一个专用的"PUI 状态机"图，不再是通用事件流编辑器。节点类型固定为三种：

- **`Entry`**（入口）：全图仅有一个，不可删除，代表状态机的起始点。只有一个输出连接器
  "初始状态"，连到哪个 `PuiState` 节点就代表状态机从哪个 PUI 开始。
- **`PuiState`**（PUI 状态）：绑定到某个具体 `.pui` 文件，通过右键菜单"添加 PUI 状态节点"
  扫描 `.puisln` 同目录下的所有 `.pui` 文件创建，不再手动选类型。
- **`Exit`**（出口）：全图仅有一个，不可删除，代表退出整个状态机。只有一个输入连接器
  "退出"，任意 `PuiState` 节点的输出连到它，就代表那个触发方式会调用
  `Polaris.PUI.PUISolution.Stop()` 结束整台状态机，而不是跳到另一个状态。

节点类型仍然走 `NodeType` 枚举 + `NodeTypeDescriptorBase` 多态工厂模式（见
`ViewModel/NodeTypes/Descriptors.cs`、`NodeTypeFactory.cs`），但这三个类型是内置语义，
不建议再新增第四种——新增业务需求应该优先考虑扩展 `PuiStateTransition`（触发方式/阻塞语义）
而不是新的节点类型。

## 输出连接器从哪来

`PuiState` 节点的输出连接器**不是手动加的**，而是它绑定的 `.pui` 文件里 Window 属性面板的
"状态连接点"列表（`PolarisTools.Pui.PuiVisualEditor.PuiStateTransition`）一一对应生成的：
一个连接点 = 一个输出连接器，标签是该连接点的 `DisplayLabel`（触发方式描述）。

`PuiStateDescriptor.CreateOutputs(object param)` 的 `param` 约定是
`IReadOnlyList<PuiStateTransition>`，由 `EditorViewModel.AddPuiStateNodeAt`/
`RefreshPuiStateNode` 读取 `.pui` 文件后传入——不是像旧版 `ListButtonDescriptor` 那样传一个
任意 `IEnumerable`。

如果对应 `.pui` 文件后来改了状态连接点列表，图上的节点不会自动同步，需要在节点右键菜单点
"刷新（重新读取 .pui）"——`RefreshPuiStateNode` 会按 `PuiStateTransition.Id` 把旧连线迁移到
新的输出连接器上，匹配不到的连线会被断开。

## 连线的语义

连线本身只表达"这个输出连接器（= 某个状态连接点）连到哪个目标 `PuiState` 节点"，不携带
触发方式或阻塞标记——那些信息都在源节点绑定的 `.pui` 文件的 `PuiStateTransition` 上（按
`ConnectorViewModel.SourceTransitionId` 关联）。这样同一个 `.pui` 可以被多个不同的
`.puisln` 图复用，各自连到不同目标。

## 编译期落地

保存 `.puisln` 时，`PolarisPuislnGenerator`（`IVsSingleFileGenerator`）会：

1. 给图里每个 `PuiState` 节点分配一个图内唯一的节点 key（同一个 `.pui` 被多个节点复用时，
   按出现顺序加 `#序号` 区分）；
2. 重新解析连线源节点绑定的 `.pui` 文件，按 `SourceTransitionId` 找到对应的
   `PuiStateTransition`，取其 `Blocking` 和 `ResolveTriggerKey()`；
3. 生成 `Foo.psg.cs`，里面是一个 `[PUISolutionAutoRegistration]` 标记的静态类
   `Foo_Solution`，其 `Definition` 属性用 `Polaris.PUI.PUIGraphDefinition.CreateBuilder(...)`
   拼出一份**不可变的图蓝图**（节点 + 边），而不是像过去那样把图拍平进一个全局静态字典。

运行时初始化时会自动发现所有带 `PUISolutionAutoRegistrationAttribute` 的类型，登记其
`Definition`，并各自调用一次 `Definition.CreateSolution()` 创建一份默认共享的
`Polaris.PUI.PUISolution` 实例——保留"编译完 `.puisln` 就能用"的零代码体验。需要额外独立实例
（比如同一张图要跑两份、互不干扰）的 mod 可以直接调用 `Foo_Solution.Definition.CreateSolution()`
或 `Foo_Solution.CreateSolution()`，每次调用都会得到一份全新的、自己维护"当前节点"的图状态机。

**这一步不支持热重载**——改一次 `.puisln` 图必须重新触发生成（保存即可）并重新编译。
`.pui` 侧"哪个按钮触发哪个 key"仍然支持热重载（见 `PuiHotReloadBridge.AddButton`）。

## 序列化

`.puisln` 走 `PuislnDocument`（`Version=2`）。旧版（`ListButton`/`ShowPUI` 等已废弃节点类型）
文件不兼容，加载时直接报错要求重建，不做静默迁移。

如需给 `PuiState`/`Entry` 之外的场景新增持久化字段：

1. 在 `NodeViewModel`/`ConnectorViewModel` 加属性；
2. 在 `PuislnNode`/`PuislnConnector` 加对应字段；
3. 在 `EditorViewModel.SaveToFile` 写入；
4. 在 `EditorViewModel.LoadFromDocument` 读取。
