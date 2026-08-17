# PolarisMagic 工具侧实现规格

本文规定 PolarisTools 对 PolarisMagic 的完整实现。实现者按本文建立文件、类型、编辑器、生成器和同步流程，不再进行格式、交互或技术选型。

## 1. 工程接入

PolarisTools 保持 net472、WPF 和 VSIX 工程。Directory.Build.props 增加 PolarisMagicDir，默认值为 $(PolarisDir)\PolarisMagic；CheckPolarisDir 同时检查 PolarisMagic\Authoring\MagicNodeCatalog.cs。路径不存在时直接终止构建并输出实际检查路径。

PolarisTools.csproj 增加 Microsoft.CodeAnalysis.CSharp 4.3.0 和 System.Text.Json 9.0.0，二者均设置 PrivateAssets=all，并由现有 IncludePackageReferenceAssembliesInVsix 规则打入 VSIX。

下列文件从 PolarisMagic 以 Compile Link 链接到 Magic\Shared，工具侧不得复制或改写第二份：

- MagicFormatVersion.cs
- MagicDefinitionDocument.cs
- MagicStateDocument.cs
- MagicNodeKind.cs
- MagicNodeCatalog.cs
- MagicGraphValidator.cs
- MagicDiagnostic.cs

这些共享文件只依赖 System、System.Collections.Generic、System.Globalization、System.Linq、System.Text.RegularExpressions、System.Xml 和 System.Xml.Linq，不引用 Visual Studio、WPF、Nodify、Unity、BepInEx、Harmony、原版程序集或 PolarisCore 运行对象。

工具代码固定放入：

    Magic/
    ├─ DefinitionEditor/
    ├─ StateEditor/
    ├─ Generation/
    ├─ CodeBehind/
    ├─ ProjectSystem/
    └─ Shared/

命名空间分别为 PolarisTools.Magic.DefinitionEditor、StateEditor、Generation、CodeBehind 和 ProjectSystem。

复用现有 PUI 基础设施的边界固定为：C# 字面量统一调用 Pui\CSharpLiteral.cs；项目项自动绑定直接扩展 PolarisToolsPackage 中的 GeneratorBindings，并继续调用现有 RunCustomTool 和 HideGeneratedFile。Magic 编辑器自行实现 MagicDocumentPaneBase 处理保存、脏状态和文件监听。PUI 的 PuiElement、PuiStateTransition、PuiCodeBehindSync 和 Pui 专用 ViewModel 不被 Magic 引用，现有 PUI 行为不改变。

## 2. Visual Studio 注册

PolarisToolsPackage 增加以下注册：

- PolarisMagicGenerator，名称 PolarisMagicGenerator，GUID f68048c2-8be0-4a86-90e3-ff6cd81ed8be，说明 Polaris Magic Source Generator。
- PmagicEditorFactory，GUID ea93f7b3-cd65-4d74-b40f-d01b7e872b15，扩展名 .pmagic，优先级 61，逻辑视图 Designer。
- PmstateEditorFactory，GUID 394822f5-5cee-4442-a3b0-996f4d2b0371，扩展名 .pmstate，优先级 61，逻辑视图 Designer。

InitializeAsync 直接注册两个工厂。两个编辑器都实现 IVsPersistDocData、IPersistFileFormat 和 IVsWindowPane，使用自身文档缓冲区读写文件，不依赖文本编辑器缓冲区。文件外部变更时使用 IVsFileChangeEx 监听；文档未修改时自动重载，已修改时显示标准 Reload/Keep Current 提示。

不为 Magic 新增工具窗口和 VSCT 命令。入口固定为双击文件、图节点右键菜单和属性编辑器按钮。

## 3. 四文件生命周期

一项魔法的文件组固定为：

    ExampleMagic.pmagic
    ExampleMagic.pmstate
    ExampleMagic.pmagic.g.cs
    ExampleMagic.pmagic.cs

文件基名必须完全一致，比较使用 OrdinalIgnoreCase；基名必须匹配 ^[A-Za-z_][A-Za-z0-9_]*$ 且不是 C# 关键字。目录也必须一致，不允许跨目录关联。

ItemTemplates\Polaris\MagicFile 新增一个多文件项模板，包含 Template.pmagic、Template.pmstate 和 Template.pmagic.cs。三个 ProjectItem 均设置 ReplaceParameters=true，文件名使用 $fileinputname$，类名使用 $safeitemname$，命名空间使用 $rootnamespace$。模板创建前三个作者文件；ExampleMagic.pmagic.g.cs 由第一次运行单文件生成器产生，模板不创建空生成文件。

模板内容固定为：

- .pmagic 写入 Version=1、字符串 Id local.magic、四个必需属性的零值和全部可选属性默认值；作者在定义编辑器中把该值改为模组自己的稳定 ID。
- .pmstate 写入 Version=1、Create、AnyState、End 以及 Create 到 End 的连线。
- .pmagic.cs 写入与文件基名一致的 public sealed partial class，并继承 MagicBehavior，不包含回调方法。

模板项目项关系固定为：

- .pmagic 是根项目项，CustomTool 为 PolarisMagicGenerator。
- .pmstate、.pmagic.cs 和生成出的 .pmagic.g.cs 的 DependentUpon 都是 ExampleMagic.pmagic。
- .pmstate 与 .pmagic.cs 的 CustomTool 为空。
- .pmagic.g.cs 的 AutoGen 和 DesignTime 均为 true，Visible 为 false。

GeneratorBindings 增加 .pmagic 绑定，GeneratedExtension 固定为 .pmagic.g.cs。Visual Studio 会先去掉源扩展名再追加 DefaultExtension，因此 ExampleMagic.pmagic 的输出恰好是 ExampleMagic.pmagic.g.cs。

PolarisToolsPackage.InitializeAsync 在现有 ItemAdded 和 DocumentSaved 订阅之外增加 ProjectItemsEvents.ItemRenamed；Dispose 对称取消订阅。ItemAdded 和 DocumentSaved 遇到 .pmagic 或 .pmstate 时都定位同名根文件并运行文件协调器；多文件模板添加 .pmagic 时图文件尚未出现只记录 PMAG1001，随后 .pmstate 的 ItemAdded 会完成首次同步和生成。ItemRenamed 只处理 .pmagic 根文件。

新增 MagicFileCoordinator，所有文件创建、保存、重命名和生成都通过它：

1. 解析根文件路径并计算三个同目录关联路径。
2. 确认 .pmagic 和 .pmstate 都存在；缺失即报告 PMAG1001 并停止生成。
3. .pmagic.cs 不存在时创建骨架并加入项目，存在时绝不整体覆盖。
4. 修正四个项目项的 DependentUpon、CustomTool、AutoGen、DesignTime 和 Visible。
5. 同步缺失回调方法。
6. 对根 .pmagic 调用 RunCustomTool。

保存 .pmagic 时直接执行上述流程。保存 .pmstate 时先保存图 JSON，再定位同名 .pmagic 并执行上述流程。保存 .pmagic.cs 不触发代码生成。

根 .pmagic 被重命名时，ProjectItemsEvents 取得旧路径和新路径；协调器只在三个旧关联文件确实属于原根项目项时将它们改成新基名。任何目标文件已存在时取消本次关联文件重命名，保留现有文件，并在 Error List 报告 PMAG1002。不会覆盖目标文件。

## 4. .pmagic 定义编辑器

PmagicEditorControl 使用单页两栏 WPF 布局。左栏是基本属性，右栏是自定义静态属性表；不存在资源页、本地化页和可插拔属性页。

左栏按以下顺序显示：Id、MpCost、CastTime、MpCrystalizeRatio、NeutralCrystalizeRatio、PrepareTime、ManaDrainLock、ProjectilePower、ShotgunRatio、SuperArmorTiredTime、DefaultAim。必需项标题后显示星号。整数使用只接受十进制整数的文本框，浮点数使用 invariant culture 数值框，DefaultAim 使用固定枚举下拉框。

右栏 DataGrid 固定四列：Name、Type、Value、Delete。Type 只提供 Int、Float、Bool、String。更改 Type 时按新类型重置 Value：Int 为 0、Float 为 0、Bool 为 false、String 为空字符串。行顺序就是生成属性顺序，可通过上移和下移按钮调整。

加载时调用 MagicDefinitionDocument.Parse。解析器使用 XmlReaderSettings，DtdProcessing=Prohibit、XmlResolver=null，并以 LoadOptions.SetLineInfo 保留字段行号；重复属性由 XML 解析错误拒绝。根元素、属性或子元素不在版本 1 封闭模式中时报告错误，不保留未知内容。保存时即使存在语义错误也将当前编辑值写成规范 XML，顺序固定为根属性、Base 属性、Properties；缩进两空格、UTF-8 无 BOM、换行 CRLF。Version 和全部可选属性始终写出。

每次控件失焦和集合变化都运行共享定义校验。错误显示在控件下方，并同步到 Error List；双击错误聚焦对应字段或属性行。

解决方案加载和 .pmagic 保存时由 MagicIdIndex 扫描已加载项目中的全部 .pmagic。字符串 Id 使用 Ordinal 比较；重复 Id 对每个文件报告 PMAG1101。未加载项目和解决方案外文件不参与索引。

## 5. .pmstate 图编辑器

StateEditor 使用 Nodify 7.3.0。固定布局为左侧节点目录、中间画布、右侧节点配置面板、底部诊断列表。画布支持选择、框选、移动、缩放、平移、连接、Delete 删除、Ctrl+C、Ctrl+V 和 Ctrl+Z/Ctrl+Y。

撤销栈保存最多 100 个 MagicStateDocument 快照。一次用户操作只产生一个快照；拖动节点从按下到释放算一次操作。保存后记录 clean revision，据此驱动 Visual Studio 脏状态。

新图固定创建：

- $create，类型 system.create，位置 40,120。
- $any，类型 system.any_state，位置 40,20。
- $end，类型 system.end，位置 360,120。
- $create.flow 到 $end.flow 的连接。

三个系统节点不能删除、复制、剪切、改型或修改 ID。普通节点创建时使用 Guid.NewGuid().ToString("D", InvariantCulture).ToLowerInvariant()。粘贴时为每个普通节点重新生成 ID，并重写粘贴集合内部的节点引用、Label 和 Jump 引用；指向集合外的 Jump 被清空并立即报错。

节点目录完全由 MagicNodeCatalog 构造，分类、名称、搜索关键字、固定端口和动态端口均不在工具侧另写 switch 表。搜索同时匹配节点显示名、英文类型 ID 和完整分类路径，使用 OrdinalIgnoreCase。

右侧配置面板按共享目录描述器生成基础编辑器，并为以下动态配置提供固定专用控件：

- Variable：ValueType 和 DefaultValue。
- Variable Equals：EqualsMode，值为 Value 或 Variable。
- Select：ValueType 只允许 Int 或 String，并编辑 Case 列表。
- Label：SymbolId。
- Jump：从当前图 Label 列表选择 SymbolId。
- CSharpCallback：CallbackId、Inputs 和 Outputs 参数表。

保存使用 System.Text.Json，PropertyNamingPolicy=CamelCase、PropertyNameCaseInsensitive=false、WriteIndented=true、DefaultIgnoreCondition=WhenWritingNull、UnmappedMemberHandling=Disallow、AllowTrailingCommas=false、ReadCommentHandling=Disallow；编码 UTF-8 无 BOM、换行 CRLF。加载前先用 Utf8JsonReader 遍历每个对象并拒绝重复属性名，同时记录 nodes 数组中每个对象的 TokenStartIndex，并通过预先建立的换行偏移表换算为一基行列，形成 MagicSourceMap。Nodes 按画布文档顺序保存，Connections 按建立顺序保存。加载后不得排序，保证中断优先级和生成文本稳定。

## 6. 端口和连接规则

每个 ConnectorViewModel 持有 NodeId、PortId、Direction、MagicPortKind、ValueType 和 MaxConnections。连接只允许 Output 到 Input，MagicPortKind 必须相同，基础值 ValueType 必须相同，VariableRef 的引用值类型也必须相同。

端口基数固定为：

- 所有数据输入、VariableRef 输入和 InterruptFlow 输入最多一条连接。
- 所有基础值输出和 VariableRef 输出允许任意数量消费者。
- 普通 StateFlow 输入允许多条汇入。
- StateFlow 输出是否允许多条由节点目录声明；Create、CSharpCallback、Variable、Dereference、Assign、变量运算、变量比较、变量逻辑、Label、ConditionalInterrupt 每个流程出口最多一条。
- AnyState.Interrupts 允许多条输出，只能连接 ConditionalInterrupt.AnyState。
- If 的 True、False 各最多一条；Select 的每个 Case 和 Default 各最多一条。

拖线期间先调用 MagicNodeCatalog.CanConnect；拒绝时光标显示禁用并在状态栏显示首条原因。加载手工编辑的非法连线时不删除文件内容，在画布上显示红线并阻止生成。

动态端口变化统一执行 RebuildPorts：按 PortId 保留仍存在且类型兼容的连接，删除其余连接并将文档标脏。具体规则固定为：

- Variable 改型时 InitialValue、Ref 和 DefaultValue 同步改型，InitialValue 的旧连接全部断开，Ref 只保留同类型消费者。
- Dereference、Assign、变量运算、变量比较和变量逻辑从接入的 VariableRef 推导值类型；引用断开时回到未定型状态并断开全部动态值端口。
- Variable Equals 以左侧 Variable 决定类型；EqualsMode 改变时重建右侧端口。
- 任意 Equals 以第一个接入的基础值确定 Int、Float、Bool 或 String；两个输入都断开时回到未定型状态；VariableRef 永远拒绝。
- Select 改型时删除全部 Case、重新创建一个值为 0 或空字符串的 Case，并断开 Value 和所有 Case 连线。
- CSharpCallback 参数使用稳定参数 ID；改名不改变端口，改型断开该参数端口连接，删除参数同时删除端口连接，排序只改变签名顺序。

## 7. 节点配置的确定规则

Variable 必须选择 Int、Float、Bool 或 String，并始终保存同类型 DefaultValue。InitialValue 未连接时使用 DefaultValue，连接时输入值覆盖 DefaultValue。

Select 至少有一个 Case。Int Case 以十进制整数比较，String Case 以 Ordinal 比较；同一 Select 中 Case 值不能重复。所有 Case 和 Default 流程出口都必须连接。

Label.SymbolId 和 CSharpCallback.CallbackId 使用正则 ^[A-Za-z_][A-Za-z0-9_]*$，并分别在图内唯一。Jump 必须选择一个现存 Label。

CSharpCallback 输入参数类型只允许 Int、Float、Bool、String、VariableRef<Int>、VariableRef<Float>、VariableRef<Bool>、VariableRef<String>；输出参数只允许 Int、Float、Bool、String。参数名使用同一 C# 标识符正则，输入和输出合并后不得重名。所有已配置输入端口都必须连接，输出引用可以不连接。

数值、比较、逻辑、Assign、Dereference、If 和 ConditionalInterrupt 的全部数据输入必须连接。流程控制节点的每一个流程出口必须连接。无连接数据输出合法。

## 8. 完整图校验

MagicGraphValidator 在加载、每次编辑、保存和生成前运行同一套规则，并一次返回全部 MagicDiagnostic。诊断包含 Code、Severity、FileKind、NodeId、PortId、PropertyName 和 Message。

校验算法固定为：

1. 校验版本、系统节点身份、普通节点 ID、节点类型、配置字段和端口存在性。
2. 校验连接方向、类型、端口基数和必需输入。
3. 把 Jump 当作指向目标 Label 的 StateFlow 边，构建主流程图。
4. 从 Create 正向标记主流程可达节点，从 End 反向标记可到达 End 的节点；每个主流程可达节点都必须在反向集合中。
5. 对主流程运行 Tarjan 强连通分量；包含两个以上节点或自环的分量必须至少有一条指向分量外且最终可达 End 的边。
6. 构建纯数据依赖图并运行深度优先着色；发现回边即报告数据环。
7. 验证每个 ConditionalInterrupt 恰好连接 AnyState、Condition 是 VariableRef<Bool>、Out 已连接。
8. 从每个中断 Out 遍历所有流程分支；合法终点只能是 End 或第 4 步已经证明可达 End 的主流程节点。中断子图同样执行强连通分量和逃离边校验。
9. 校验 Label、Jump、CallbackId、参数名、Case 值和字符串魔法 Id 的唯一性。

编辑器允许保存带错误的作者文件，便于修复；只要存在 Error 级诊断，生成器返回 E_FAIL，调用 IVsGeneratorProgress.GeneratorError 写入 Error List，并且 Visual Studio 保留上一次成功的 .pmagic.g.cs 内容。

## 9. .pmagic.g.cs 生成器

PolarisMagicGenerator 实现 IVsSingleFileGenerator。DefaultExtension 固定返回 .pmagic.g.cs。Generate 只接受 .pmagic，使用 bstrInputFileContents 解析定义，并从同目录读取同名 .pmstate；文件协调器保证图编辑器先完成落盘。

生成顺序固定为：

1. 解析两份作者文件并运行完整校验，再用 MagicCodeBehindSync.Validate 确认同名 .pmagic.cs、目标 partial 类和全部回调签名存在且匹配。
2. 类名取 .pmagic 的文件基名；命名空间使用 IVsSingleFileGenerator 收到的 wszDefaultNamespace，空值使用 Polaris.Generated。
3. CSharpCallback 按 Nodes 文档顺序分配从 0 开始的 callbackIndex。
4. 变量槽位按 Nodes 文档顺序分配；同一节点的输出按 Outputs 顺序继续分配。
5. 节点和连接保持文档顺序发射。
6. 使用 MagicCSharpEmitter 按 CRLF 输出 UTF-8 文本；整数十进制，Float 使用 R 格式并追加 f，字符串通过现有 CSharpLiteral.Escape 转义。

生成文件固定包含：

- auto-generated 文件头和必要 using。
- 带 MagicDefinitionProviderAttribute 的 internal sealed 提供器，类型名固定为魔法类名加 __PolarisMagicProvider。
- public sealed partial 魔法类，继承 MagicBehavior。
- Id 字符串常量、全部基本属性和自定义静态属性的 public static 只读属性。
- internal static BuildDefinition 方法，使用 MagicDefinitionBuilder 写入基本属性、行为工厂和完整图定义。
- protected override InvokeCallback 方法。

BuildDefinition 固定把 typeof(生成提供器).Assembly.FullName 传给 SetProviderAssembly，把 static () => new 当前魔法类() 传给 SetBehaviorFactory，并把 MagicGraphBuilder.Build 的结果传给 SetGraph；最后只调用一次 MagicDefinitionBuilder.Build。

InvokeCallback 使用 callbackIndex switch，不使用反射。每个 case 严格按输入顺序调用 frame.GetInputInt、GetInputFloat、GetInputBool、GetInputString 或 GetInputRef<T>；out 参数先写入局部变量，调用 Callback_加CallbackId；返回 false 时直接返回 false，不写暂存区；返回 true 时按输出顺序调用 SetOutputInt、SetOutputFloat、SetOutputBool 或 SetOutputString，并返回 true。未知索引抛 InvalidOperationException。

图生成使用 MagicGraphBuilder 的公开 AddNode 和 Connect 接口。每个节点都发射稳定 NodeId、MagicNodeKind、类型配置、符号配置、回调索引、参数配置和 Case 配置；每条连接都发射 fromNode、fromPort、toNode、toPort。BuildDefinition 最后调用 builder.Build，运行时不解析 XML 或 JSON。

生成文本开头为 .pmagic 属性写 #line，图中每个 AddNode 调用前为 .pmstate 对应节点写 #line；节点完成后使用 #line default。诊断仍以结构化 NodeId 定位，#line 只负责 C# 编译错误回指。

生成全部在内存完成。只有返回 S_OK 时把完整字节数组交给 Visual Studio；异常、I/O 错误或校验错误返回 E_FAIL，不返回部分缓冲区。

## 10. .pmagic.cs code-behind 同步

MagicCodeBehindSync 使用 Microsoft.CodeAnalysis.CSharp 解析现有文件。初始骨架固定为：

    using Polaris.Magic.Runtime;

    namespace 项目默认命名空间;

    public sealed partial class 文件基名 : MagicBehavior
    {
    }

MagicFileCoordinator 先读取 .pmagic 项目的 CustomToolNamespace，非空时使用该值，否则读取项目 DefaultNamespace；两者都为空时使用 Polaris.Generated。命名空间的每个点分段必须匹配 ^[A-Za-z_][A-Za-z0-9_]*$ 且不是 C# 关键字。该结果必须与生成器收到的 wszDefaultNamespace 一致，不一致时报 PMAG1303 并停止生成。骨架只在文件不存在时创建一次。

回调方法固定生成在该 partial 类内，格式为 private bool Callback_加CallbackId。普通输入生成 int、float、bool、string；引用输入生成 MagicVariableRef<T>；输出生成对应基础类型的 out 参数。方法体先为每个 out 参数赋 default，再返回 true。

同步算法固定为：

1. 用语法树按命名空间、类名、partial 修饰符定位唯一目标类；不存在或多于一个时报 PMAG1301。
2. 以方法名查找成员。没有方法时在目标类闭合大括号前插入一个 CRLF 和完整方法文本。
3. 存在一个方法且返回类型、访问级别、参数顺序、参数名、参数类型和 ref kind 完全一致时不修改。
4. 存在同名但签名不一致的方法时报 PMAG1302，不新增重载，不改写用户方法。
5. 图中删除回调时不删除已有方法。修改 CallbackId 等同于新增新方法，旧方法保留。

插入操作使用语法树 Span 找到目标位置，但不调用 NormalizeWhitespace，不重排 using，不格式化用户代码，不修改已有字符。写入使用原文件 BOM 和换行风格；新文件使用 UTF-8 无 BOM 和 CRLF。

双击 CSharpCallback 节点先运行同步；成功后通过 VsShellUtilities.OpenDocument 打开 .pmagic.cs，再用 IVsTextManager.NavigateToLineAndColumn 定位该方法标识符。签名冲突时打开冲突方法并选中方法名。

## 11. 诊断与用户可见行为

MagicDiagnosticPresenter 同时维护编辑器诊断列表和 Visual Studio Error List。编辑器侧使用一个包级 ErrorListProvider，按规范化绝对文件路径替换该文件的 ErrorTask 集合；生成器侧另外调用 IVsGeneratorProgress.GeneratorError。ErrorTask 的 Document、Line、Column、ErrorCategory、Text 和 Navigate 委托全部从 MagicDiagnostic 与 MagicSourceMap 填充。错误码范围固定为：

- PMAG1000–1099：文件组、版本和 I/O。
- PMAG1100–1199：.pmagic 格式与字符串 Id。
- PMAG1200–1299：图、节点、端口、类型和流程。
- PMAG1300–1399：code-behind 和回调签名。
- PMAG1400–1499：生成器内部一致性。

版本 1 实际使用的代码固定为：PMAG1001 缺失关联文件，PMAG1002 重命名冲突，PMAG1003 高版本只读，PMAG1004 缺失或低版本，PMAG1005 XML/JSON/I/O 失败；PMAG1101 重复字符串 Id，PMAG1102 非法 Id，PMAG1103 缺少必需属性，PMAG1104 属性类型或范围错误，PMAG1105 重复自定义属性，PMAG1106 非法 C# 名称；PMAG1201 系统节点错误，PMAG1202 节点 ID 或类型错误，PMAG1203 节点配置错误，PMAG1204 端口不存在，PMAG1205 连接方向或类型错误，PMAG1206 连接基数错误，PMAG1207 必需输入或流程出口未连接，PMAG1208 孤立流程节点，PMAG1209 流程不能到达 End，PMAG1210 无逃离路径的状态流环，PMAG1211 数据环，PMAG1212 Label 或 Jump 错误，PMAG1213 条件中断错误，PMAG1214 CallbackId、参数或 Case 重复；PMAG1301 partial 类定位失败，PMAG1302 回调签名冲突，PMAG1303 命名空间不一致；PMAG1401 生成模型与共享目录不一致，PMAG1402 未处理的生成器异常。同一元素同时违反多条规则时分别报告，不用一个错误覆盖另一个。

双击定义诊断打开 .pmagic 并聚焦字段；双击图诊断打开 .pmstate、选中节点并居中；双击 code-behind 诊断定位方法。无法定位具体元素时定位文件首行。

格式 Version 大于 1 时编辑器以只读模式打开并报告 PMAG1003；Version 小于 1 或缺失时作为损坏文件报告 PMAG1004。工具不会自动升级或降级格式。

## 12. 测试和验收

新增 PolarisTools.Magic.Tests，目标 net472，使用 xUnit 2.9.2、xunit.runner.visualstudio 2.8.2 和 Microsoft.NET.Test.Sdk 17.12.0。VS SDK 交互封装为 IMagicProjectSystem，测试使用内存实现；解析、校验、生成和 code-behind 同步不启动 devenv。

必须具备以下测试：

- .pmagic 规范 XML 往返、所有基本属性、自定义属性四种类型、未知字段和重复 Id。
- .pmstate 每一种目录节点的创建、动态端口、序列化和非法配置。
- 每一种允许连接和拒绝连接；所有动态改型的保留与断线行为。
- Create 到 End、分支、Jump、可逃离环、死环、数据环、中断到 End 和中断合流。
- 回调索引、变量槽位、参数顺序、字面量转义和两次生成字节完全相同。
- code-behind 首次创建、缺失方法追加、匹配方法保留、冲突签名诊断、删除节点不删方法。
- 模板创建后三个作者文件和第一次生成后的四文件嵌套关系。
- 现有 PUI、Puisln、Plang 生成器和 VSIX 打包回归。

验收时在 Visual Studio Experimental Instance 中创建一个新魔法，编辑定义、建立包含所有节点类别的图、生成回调桩并构建示例模组。输出必须只有一个 .pmagic.g.cs，运行时程序集不得读取作者 XML 或 JSON，用户已有 .pmagic.cs 的任何方法体不得发生变化。
