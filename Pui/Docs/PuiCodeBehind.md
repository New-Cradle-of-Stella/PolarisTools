# .pui 文件是怎么变成 C# 代码的

每次你添加或保存一个 `Foo.pui` 文件，插件都会自动帮你生成/更新两个 C# 文件。这两个文件是**同一个类**（`public partial class FOO`）的两半，用途完全不同，要分清楚。

## 两个文件分别是干什么的

| 文件 | 要不要看/改 | 干什么的 |
|------|------------|----------|
| `Foo.g.cs` | **不需要** | 全部由插件根据 `Foo.pui` 里搭的界面自动写出来的代码：`GetUIWindow`（建面板）和 `BuildUI`（往面板里加文字/按钮/分割线等）。每次保存 `Foo.pui` 都会被重新覆盖。在"解决方案资源管理器"里默认是隐藏的，你基本不会看到它。 |
| `Foo.pui.cs` | **需要** | 第一次创建时是空骨架，以后永远不会被整体覆盖。你要写的交互逻辑（比如按钮点击之后要做什么）都写在这里面。 |

简单说：`Foo.g.cs` 你不用管，`Foo.pui.cs` 才是你自己的代码文件。

## 两者是怎么连起来的

`Foo.g.cs` 和 `Foo.pui.cs` 声明的是**同一个类名**（`public partial class FOO : IPUI { ... }`），靠 C# 的 `partial class` 拼在一起，不是继承关系：

- `Foo.g.cs`：`Name` / `GetUIWindow` / `BuildUI` 三个 `IPUI` 成员，内容由 `Foo.pui` 里的元素树直接翻译过来（一个 `Text` 元素对应一行 `designer.P(...)`，一个 `Button` 元素对应一行 `designer.addButton(...)`，以此类推）。
- `Foo.pui.cs`：你自己的代码，主要是按钮的点击回调方法。

这样设计的好处：`Foo.pui` 改名成 `Bar.pui` 之后，两个文件里的类名会**一起**变成 `BAR`（因为类名固定是文件名的大写形式，两侧各自独立计算，不存在"一个引用另一个的名字"），不会出现"改名后编译报错，要去另一个文件手动改类名"的老问题。

## 回调/配置钩子是怎么接上的

不只是按钮的点击，`.pui` 里每种控件都可能需要挂一段代码——单选组选中项变了要通知谁、滑块拖动之后要做什么、颜色格选完颜色要干什么、图像控件要显示哪张图。这些统一靠几个同名约定的属性表达，机制都一样：属性值是方法名，生成器在 `Foo.g.cs` 里把这个方法名接到对应的委托字段上，`Foo.pui.cs` 里缺这个方法就自动追加一个桩。

| 属性 | 出现在哪些控件上 | 桩方法签名（`XX.` 前缀省略的都在 `XX` 命名空间里） |
| --- | --- | --- |
| `OnClick` | Button、ButtonMulti、Checks、Radio、Slider、NumCounter | `bool 方法名(XX.aBtn _B)` |
| `OnChanged` | Radio（选中项变化）、Slider（数值变化）、Input（内容变化） | Radio: `bool 方法名(XX.BtnContainerRadio<XX.aBtn> container, int previous, int current)`；Slider: `bool 方法名(XX.aBtnMeter button, float previous, float current)`；Input: `bool 方法名(XX.LabeledInputField field)` |
| `OnChangedDelay` | Input（延迟变化，用于减少高频回调） | `bool 方法名(XX.LabeledInputField field)` |
| `OnColorChanged` | ColorCell | `bool 方法名(XX.aBtnColorCell button, UnityEngine.Color32 previous, UnityEngine.Color32 current)` |

> `Image` 控件要显示的 `MI`/`PF` 图集/帧数据是运行时对象，没法用 XML 字符串表达，目前也没有别的钩子能在代码里手动赋值——`Image` 控件暂时不会显示任何图案。

**这些方法桩都不需要你手写**：只要 `.pui` 里某个属性指向的方法名在 `.pui.cs` 里还不存在，插件保存时会按上表的签名自动把桩追加到 `.pui.cs` 的末尾（`bool` 类型默认 `return false;`，`void` 类型默认空方法体），你只要把方法体改成真正的逻辑就行。已经存在的方法（不管是不是自动加的）永远不会被改动或删除，多个控件复用同一个方法名时也只会生成一份桩。

## 什么时候会重新生成 / 追加

只要你保存 `Foo.pui`：

- `Foo.g.cs` 会被整个重新生成一次，这是正常现象，不用担心——反正你也不会去改那个文件。
- `Foo.pui.cs` 只会被"追加缺失的 OnClick 方法桩"，已有内容原样保留，不会被覆盖。

## 如果不小心删掉了 `.pui.cs`

`Foo.pui.cs` 是第一次保存 `Foo.pui` 的时候自动创建出来的。如果你把它删了，下次保存 `Foo.pui`，它会被重新创建成一个空骨架（外加当时 `Foo.pui` 里已有按钮对应的 OnClick 桩）——里面原来写的代码不会恢复，所以不要手动删它。
