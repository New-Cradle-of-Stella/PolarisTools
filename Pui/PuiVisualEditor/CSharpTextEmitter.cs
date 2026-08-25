using Polaris.Localization;
using Polaris.UI;
using Polaris.UI.Wire;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace PolarisTools.Pui.PuiVisualEditor;

/// <summary>
/// <see cref="IPuiEmitter"/> 的编译期实现：把 <see cref="PuiTreeWalker"/> 遍历出来的调用
/// 拼成 .g.cs 里 GetUIWindow/BuildUI 方法体的 C# 源码文本。这是从原来
/// PolarisPuiGenerator.BuildGetUIWindowBody/BuildBuildUIBody/AppendStatement 里原样搬过来的
/// 字符串拼接逻辑，只是入口从"直接读 PuiElement 字段"改成了"读 walker 传来的已解析参数"，
/// 拼出来的文本应与重构前逐字节一致。
/// </summary>
internal sealed class CSharpTextEmitter : IPuiEmitter
{
    private readonly List<string> lines = new List<string>();
    private readonly List<string> extraMembers = new List<string>();
    private int seq;

    public string GetUIWindowBody { get; private set; } = "";

    /// <summary>
    /// 状态转换触发包装方法（见 <see cref="AddButton"/>），追加在 BuildUI 之后、类体结束之前。
    /// 没有任何按钮配置状态连接点时为空字符串，不产生任何多余内容。
    /// </summary>
    public string ExtraMembers => extraMembers.Count == 0 ? "" : "\n\n" + string.Join("\n\n", extraMembers);

    public void CreateWindow(string name, double pixelX, double pixelY, double width, double height, int appearDir, double appearLen, PuiMaskType mask)
    {
        string maskEnum = "UiBoxDesignerFamily.MASKTYPE." + MaskToEnum(mask);
        GetUIWindowBody = "        return source.Create(" +
            $"\"{Esc(name)}\", {F(pixelX)}, {F(pixelY)}, {F(width)}, {F(height)}, " +
            $"{appearDir}, {F(appearLen)}, {maskEnum});";
    }

    public void SetFrameType(PuiFrameType frameType)
        => lines.Add($"designer.getBox().frametype = UiBox.FRAMETYPE.{FrameTypeToEnum(frameType)};");

    public void SetFocusable() => lines.Add("designer.Focusable();");

    public void AddText(PuiTextParams p)
    {
        string varName = NextVar();
        lines.Add($"var {varName} = new DsnDataP {{ name = \"{Esc(p.Name)}\", text = {StrExpr(p.Text)}, " +
            $"alignx = ALIGN.{TextAlignToEnum(p.Align)}, swidth = {F(p.Width)}, sheight = {F(p.Height)}, html = {B(p.Html)}, " +
            $"size = {F(p.Size)}, lineSpacing = {F(p.LineSpacing)}, letterSpacing = {F(p.LetterSpacing)}, " +
            $"TxCol = {ColorLiteral(p.TextColor)}, Col = {ColorLiteral(p.BackgroundColor)}, " +
            $"TxBorderCol = {ColorLiteral(p.BorderColor)} }};");
        lines.Add($"designer.addP({varName});");
    }

    public void AddButton(PuiButtonParams p)
    {
        string varName = NextVar();
        var props = new List<string>();
        if (!string.IsNullOrEmpty(p.Name)) props.Add($"name = \"{Esc(p.Name)}\"");
        // 判空判的是**原始串**（不是解析后的结果），热重载侧 PuiHotReloadBridge.AddButton
        // 同样如此：即使某个键解析出空文案，两条路径的行为也还是一样的。
        if (!string.IsNullOrEmpty(p.Title)) props.Add($"title = {StrExpr(p.Title)}");
        if (!string.IsNullOrEmpty(p.Skin)) props.Add($"skin = \"{Esc(p.Skin)}\"");
        props.Add($"w = {F(p.Width)}");
        props.Add($"h = {F(p.Height)}");
        string fnClickRef = ResolveFnClick(p.Name, p.OnClick, p.TransitionTriggerKey);
        if (fnClickRef != null) props.Add($"fnClick = {fnClickRef}");
        lines.Add($"var {varName} = new DsnDataButton {{ {string.Join(", ", props)} }};");
        lines.Add($"designer.addButton({varName});");
    }

    /// <summary>
    /// 按钮同时是某条状态连接点的触发点时，不能再直接把 fnClick 指向用户的 OnClick 方法——
    /// 需要包一层私有方法：先调用原 OnClick（如果有），再调用 PUIRuntime.Of(this)?.RaiseEvent
    /// 把触发 key 喊出去（会被路由给当前拥有本实例的 PUISolution），最后把原 OnClick 的返回值
    /// 原样传回去。包装方法追加进 <see cref="extraMembers"/>，由 <see cref="ExtraMembers"/>
    /// 输出到类体末尾。没有状态连接点时行为完全不变。
    /// </summary>
    private string ResolveFnClick(string elementName, string onClick, string transitionTriggerKey)
    {
        if (string.IsNullOrEmpty(transitionTriggerKey))
            return string.IsNullOrEmpty(onClick) ? null : onClick;

        string wrapperName = $"__FireTransition_{CSharpLiteral.SanitizeIdentifier(elementName)}";
        string callUser = string.IsNullOrEmpty(onClick) ? "true" : $"{onClick}(_B)";
        extraMembers.Add(
            $"    private bool {wrapperName}(XX.aBtn _B)\n" +
            "    {\n" +
            $"        bool __r = {callUser};\n" +
            $"        Polaris.UI.PUIRuntime.Of(this)?.RaiseEvent(\"{Esc(transitionTriggerKey)}\");\n" +
            "        return __r;\n" +
            "    }");
        return wrapperName;
    }

    public void AddSeparator(PuiSeparatorParams p)
    {
        string varName = NextVar();
        lines.Add($"var {varName} = new DsnDataHr {{ swidth = {F(p.Width)}, vertical = {B(p.Vertical)}, " +
            $"line_height = {F(p.LineHeight)}, margin_t = {F(p.MarginBefore)}, margin_b = {F(p.MarginAfter)}, " +
            $"dashed_oneline_lgt = {F(p.DashedLength)}, draw_width_rate = {F(p.DrawWidthRate)}, " +
            $"Col = {ColorLiteral(p.Color)} }};");
        lines.Add($"designer.addHr({varName});");
    }

    // 原生成器的 AppendStatement 在 switch 之前就无条件 varName = $"__d{seq++}"，
    // 即使 LineBreak/LineStyle/DefaultLineStyle 用不到这个变量名也照样把序号消耗掉；
    // 这里同样无条件调用 NextVar() 并丢弃结果，保证后续元素的 __dN 编号跟重构前一致。
    public void Br()
    {
        NextVar();
        lines.Add("designer.Br();");
    }

    public void SetLineAlign(PuiLineAlign align)
    {
        NextVar();
        lines.Add($"designer.alignx = ALIGN.{LineAlignToEnum(align)};");
    }

    public void SetDefaultLineAlign()
    {
        NextVar();
        lines.Add("designer.alignx = ALIGN.LEFT;");
    }

    public void AddButtonMulti(PuiButtonMultiParams p)
    {
        string varName = NextVar();
        lines.Add($"var {varName} = new DsnDataButtonMulti {{ name = \"{Esc(p.Name)}\", titles = {ListExpr(p.Titles)}, " +
            $"skin = \"{Esc(p.Skin)}\", w = {F(p.Width)}, h = {F(p.Height)}, clms = {p.Columns}, " +
            $"margin_w = {F(p.MarginW)}, margin_h = {F(p.MarginH)}, navi_loop = {p.NaviLoop}, " +
            $"def = {p.DefMask}, locked = {p.LockedMask}" +
            (string.IsNullOrEmpty(p.OnClick) ? "" : $", fnClick = {p.OnClick}") + " };");
        lines.Add($"designer.addButtonMulti({varName});");
    }

    public void AddChecks(PuiChecksParams p)
    {
        string varName = NextVar();
        lines.Add($"var {varName} = new DsnDataChecks {{ name = \"{Esc(p.Name)}\", keys = {ListLiteral(p.Keys)}, " +
            // descs 是显示给玩家的说明文字，走 ListExpr；同一行的 keys 是回调返回值用的
            // 标识符，保持 ListLiteral 不解析。
            (p.Descs == null ? "" : $"descs = {ListExpr(p.Descs)}, ") +
            $"skin = \"{Esc(p.Skin)}\", w = {F(p.Width)}, h = {F(p.Height)}, scale = {F(p.Scale)}, " +
            $"clms = {p.Columns}, margin_w = {p.MarginW}, margin_h = {p.MarginH}, navi_loop = {p.NaviLoop}, " +
            $"def = {p.DefMask}" +
            (string.IsNullOrEmpty(p.OnClick) ? "" : $", fnClick = {p.OnClick}") + " };");
        lines.Add($"designer.addChecks({varName});");
    }

    public void AddRadio(PuiRadioParams p)
    {
        string varName = NextVar();
        string radioInit = $"new DsnDataRadio {{ name = \"{Esc(p.Name)}\", keys = {ListLiteral(p.Keys)}, " +
            // descs 是显示给玩家的说明文字，走 ListExpr；同一行的 keys 是回调返回值用的
            // 标识符，保持 ListLiteral 不解析。
            (p.Descs == null ? "" : $"descs = {ListExpr(p.Descs)}, ") +
            $"skin = \"{Esc(p.Skin)}\", w = {F(p.Width)}, h = {F(p.Height)}, clms = {p.Columns}, scale = {F(p.Scale)}, " +
            $"margin_w = {p.MarginW}, margin_h = {p.MarginH}, def = {p.Def}, " +
            $"value_return_name = {B(p.ValueReturnName)}, all_function_same = {B(p.AllFunctionSame)}, navi_loop = {p.NaviLoop}" +
            (string.IsNullOrEmpty(p.OnClick) ? "" : $", fnClick = {p.OnClick}") +
            (string.IsNullOrEmpty(p.OnChanged) ? "" : $", fnChanged = {p.OnChanged}") + " }";
        if (p.RowMode)
            radioInit += $".RowMode(\"{Esc(p.Skin)}\")";
        lines.Add($"var {varName} = {radioInit};");
        lines.Add($"designer.addRadio({varName});");
    }

    public void AddSlider(PuiSliderParams p)
    {
        string varName = NextVar();
        // title 是滑条标题（显示用）；同方法下面的 Adesc_keys 名字即 keys，不在本地化范围内。
        lines.Add($"var {varName} = new DsnDataSlider {{ name = \"{Esc(p.Name)}\", title = {StrExpr(p.Title)}, " +
            $"skin = \"{Esc(p.Skin)}\", skin_title = \"{Esc(p.SkinTitle)}\", mn = {F(p.Min)}, mx = {F(p.Max)}, " +
            $"valintv = {F(p.Step)}, w = {F(p.Width)}, h = {F(p.Height)}, def = {F(p.Def)}, " +
            $"submit_holding = {B(p.SubmitHolding)}, checkbox_mode = {p.CheckboxMode}, " +
            $"Adesc_keys = {ListLiteral(p.DescKeys)}" +
            (string.IsNullOrEmpty(p.OnClick) ? "" : $", fnClick = {p.OnClick}") +
            (string.IsNullOrEmpty(p.OnChanged) ? "" : $", fnChanged = {p.OnChanged}") + " };");
        lines.Add($"designer.addSliderCT({varName}, {F(p.SetterWidth)});");
    }

    public void AddInput(PuiInputParams p)
    {
        string varName = NextVar();
        // def 是输入框初始值（数据，不是标签），保持字面量；label 才是显示给玩家的那行字。
        lines.Add($"var {varName} = new DsnDataInput {{ name = \"{Esc(p.Name)}\", def = \"{Esc(p.Def)}\", " +
            $"label = {StrExpr(p.Label)}, skin = \"{Esc(p.Skin)}\", w = {F(p.Width)}, bounds_w = {F(p.BoundsWidth)}, " +
            $"size = {p.FontSize}, h = {F(p.Height)}, max_len = {p.MaxLen}, min = {D(p.Min)}, max = {D(p.Max)}, " +
            $"integer = {B(p.Integer)}, hex_integer = {B(p.HexInteger)}, number = {B(p.Number)}, " +
            $"multi_line = {p.MultiLine}, label_top = {B(p.LabelTop)}, return_blur = {B(p.ReturnBlur)}, " +
            $"editable = {B(p.Editable)}, alloc_empty = {B(p.AllocEmpty)}, changed_delay_maxt = {p.ChangedDelayMaxT}" +
            (string.IsNullOrEmpty(p.OnChanged) ? "" : $", fnChanged = {p.OnChanged}") +
            (string.IsNullOrEmpty(p.OnChangedDelay) ? "" : $", fnChangedDelay = {p.OnChangedDelay}") + " };");
        lines.Add($"designer.addInput({varName});");
    }

    public void AddNumCounter(PuiNumCounterParams p)
    {
        string varName = NextVar();
        lines.Add($"var {varName} = new DsnDataNumCounter {{ name = \"{Esc(p.Name)}\", def = {p.Def}, " +
            $"locked = {B(p.Locked)}, skin = \"{Esc(p.Skin)}\", w = {F(p.Width)}, h = {F(p.Height)}, " +
            $"navi_loop = {p.NaviLoop}, minval = {p.MinVal}, maxval = {p.MaxVal}, digit = {p.Digit}, " +
            $"slide_cur_digit_only = {B(p.SlideCurDigitOnly)}" +
            (string.IsNullOrEmpty(p.OnClick) ? "" : $", fnClick = {p.OnClick}") + " };");
        lines.Add($"designer.addNumCounterT<XX.aBtnNumCounter>({varName});");
    }

    public void AddColorCell(PuiColorCellParams p)
    {
        string varName = NextVar();
        lines.Add($"var {varName} = new DsnDataColorCell {{ name = \"{Esc(p.Name)}\", def = {ColorLiteral(p.DefColor)}, " +
            $"open_prompt = {B(p.OpenPrompt)}, use_text = {B(p.UseText)}, use_alpha = {B(p.UseAlpha)}, " +
            $"title = {StrExpr(p.Title)}, skin = \"{Esc(p.Skin)}\", skin_title = \"{Esc(p.SkinTitle)}\", " +
            $"w = {F(p.Width)}, h = {F(p.Height)}" +
            (string.IsNullOrEmpty(p.OnColorPromptDone) ? "" : $", fnPromptDone = {p.OnColorPromptDone}") + " };");
        lines.Add($"designer.addColorCell({varName});");
    }

    public void AddImage(PuiImageParams p)
    {
        string varName = NextVar();
        lines.Add($"var {varName} = new DsnDataImg {{ name = \"{Esc(p.Name)}\", swidth = {F(p.Width)}, sheight = {F(p.Height)}, " +
            $"stencil_lessequal = {B(p.StencilLessEqual)} }};");

        // MI 之外的 UvRect/scale 不直接填：DsnDataImg 的 UvRect 其实是"纹理像素矩形"、绘制尺寸
        // 只由 UvRect 尺寸 × scale 决定（跟 swidth/sheight 无关），照字面填只会画出一个 1px 的点。
        // 换算统一交给运行时的 Polaris.PUI.PuiImage.Assign——热重载走的是同一个方法，两条路径
        // 不会一边对一边错。
        string imageExpr = null;
        if (!string.IsNullOrEmpty(p.ImageResource))
        {
            // 资源字段引用（属性面板里选的那个 [PolarisResource] MImage static 字段）：直接读字段，
            // 不再走一次挂载表查询——PolarisRes 的 AutoBindScanner 在插件加载时就已经按
            // [PolarisResourceFolder] 挂好目录、把 MImage 填进这个字段了。加 global:: 前缀，
            // 免得跟 .pui.cs 所在命名空间里的同名类型撞车。
            imageExpr = $"global::{p.ImageResource}";
        }
        else if (!string.IsNullOrEmpty(p.ImageSource))
        {
            // 早期写法（手写 XML 才可能有）：ImageSource 是 PolarisRes 挂载相对路径；modId 用当前
            // 程序集名——和 AutoBindScanner 用 assembly.GetName().Name 当 modId 的约定保持一致。
            // 注意它查的是 modId 那张共享挂载表，需要模组自己 Mount/MountDefault 过；自动绑定用的
            // 是按类分开的表，两者不通。Own.Image 按路径去重缓存，重复 BuildUI 不会重复解码。
            imageExpr = $"Polaris.Res.PolarisResAPI.For(System.Reflection.Assembly.GetExecutingAssembly().GetName().Name).Own.Image(\"{Esc(p.ImageSource)}\")";
        }

        if (imageExpr != null)
        {
            lines.Add($"global::Polaris.UI.PuiImage.Assign({varName}, {imageExpr}, " +
                $"{F(p.UvX)}, {F(p.UvY)}, {F(p.UvW)}, {F(p.UvH)}, {F(p.Width)}, {F(p.Height)}, {F(p.Scale)});");
        }

        lines.Add($"designer.addImg({varName});");
    }

    public void AddCustom(PuiCustomParams p)
    {
        if (string.IsNullOrEmpty(p.BackendType))
        {
            throw new InvalidOperationException($"Custom element \"{p.Name}\" has no BackendType set; pick a type implementing Polaris.UI.IPuiCustomControl in the property panel.");
        }

        string varName = NextVar();
        lines.Add($"var {varName} = new DsnDataImg {{ name = \"{Esc(p.Name)}\", swidth = {F(p.Width)}, sheight = {F(p.Height)} }};");
        string blockVar = NextVar();
        lines.Add($"var {blockVar} = designer.addImg({varName});");
        lines.Add($"global::Polaris.UI.PuiCustomControl.Attach({varName}, {blockVar}, new global::{p.BackendType}(), {F(p.Width)}, {F(p.Height)});");
    }

    public void OnBuildCompleted(string methodName) => lines.Add($"{methodName}(designer);");

    public string BuildUIBody()
    {
        if (lines.Count == 0)
            return "";

        var sb = new StringBuilder();
        foreach (string line in lines)
            sb.Append("        ").Append(line).Append('\n');
        return sb.ToString().TrimEnd('\n');
    }

    private string NextVar() => $"__d{seq++}";

    private static string MaskToEnum(PuiMaskType mask) => mask switch
    {
        PuiMaskType.NoMask => "NO_MASK",
        PuiMaskType.Scroll => "SCROLL",
        _ => "BOX",
    };

    private static string FrameTypeToEnum(PuiFrameType frame) => frame switch
    {
        PuiFrameType.None => "NONE",
        PuiFrameType.OneLine => "ONELINE",
        PuiFrameType.Dark => "DARK",
        PuiFrameType.DarkSimple => "DARK_SIMPLE",
        PuiFrameType.NoOverride => "NO_OVERRIDE",
        _ => "MAIN",
    };

    private static string TextAlignToEnum(PuiTextAlign align) => align switch
    {
        PuiTextAlign.Center => "CENTER",
        PuiTextAlign.Right => "RIGHT",
        PuiTextAlign.Auto => "_AUTO",
        _ => "LEFT",
    };

    private static string LineAlignToEnum(PuiLineAlign align) => align switch
    {
        PuiLineAlign.Center => "CENTER",
        PuiLineAlign.Right => "RIGHT",
        _ => "LEFT",
    };

    // Designer 里的坐标/尺寸参数大多是 float，double 字面量必须带 f 后缀才能隐式传进去。
    private static string F(double value) => value.ToString(CultureInfo.InvariantCulture) + "f";

    // 少数字段是 double（比如 DsnDataInput.min/max），不能带 f 后缀。
    private static string D(double value) => value.ToString(CultureInfo.InvariantCulture);

    private static string B(bool value) => value ? "true" : "false";

    private static string Esc(string value) => CSharpLiteral.Escape(value);

    /// <summary>
    /// 显示用字符串 → C# 表达式。以 <c>&amp;</c> 开头的按本地化键处理，生成
    /// <c>global::XX.TX.Get("key")</c>（调用点在 BuildUI 体内，每次重建窗口都会重新取值，
    /// 切语言后下一次开窗自然生效）；开头 <c>&amp;&amp;</c> 是字面 <c>&amp;</c> 的转义，
    /// 脱一层后照常生成普通字面量。
    /// <para>
    /// 判定放在编译期而不是生成一个运行时调用：字符串在这里已经是已知常量，不含
    /// <c>&amp;</c> 的 .pui 生成结果因此和加本功能之前<b>逐字节一致</b>，既有项目重新
    /// 生成不会产生 diff。判定本身用的是跟热重载侧 <c>PuiText</c> 同一份
    /// <see cref="LocalizedString"/>，两条路径不会各走一套规则。
    /// </para>
    /// </summary>
    private static string StrExpr(string value)
        => LocalizedString.TryGetKey(value, out string key)
            ? $"global::XX.TX.Get(\"{Esc(key)}\")"
            : $"\"{Esc(LocalizedString.Unescape(value))}\"";

    private static string ColorLiteral(PuiColor c) =>
        $"new UnityEngine.Color32({c.R}, {c.G}, {c.B}, {c.A})";

    /// <summary>把字符串数组转成 C# string[] 字面量；null/空数组生成 null。</summary>
    private static string ListLiteral(string[] items)
    {
        if (items == null || items.Length == 0)
            return "null";

        var quoted = new List<string>(items.Length);
        foreach (string item in items)
            quoted.Add($"\"{Esc(item)}\"");

        return "new string[] { " + string.Join(", ", quoted) + " }";
    }

    /// <summary>
    /// <see cref="ListLiteral"/> 的显示用版本：逐项走 <see cref="StrExpr"/>，所以同一个数组
    /// 里可以键和字面量混着来（<c>new string[] { global::XX.TX.Get("a"), "B" }</c>）。
    /// null/空数组同样生成 null，跟 <see cref="ListLiteral"/> 保持一致。
    /// </summary>
    private static string ListExpr(string[] items)
    {
        if (items == null || items.Length == 0)
            return "null";

        var exprs = new List<string>(items.Length);
        foreach (string item in items)
            exprs.Add(StrExpr(item));

        return "new string[] { " + string.Join(", ", exprs) + " }";
    }
}
