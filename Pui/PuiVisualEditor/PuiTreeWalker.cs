using Polaris.PUI.Wire;
using System;
using System.Collections.Generic;

namespace PolarisTools.Pui.PuiVisualEditor;

/// <summary>
/// 唯一一份"读 <see cref="PuiElement"/> 树，决定该发出哪个 <see cref="IPuiEmitter"/> 调用、
/// 参数怎么取默认值"的逻辑；从原来 PolarisPuiGenerator 里直接拼 C# 文本的
/// BuildGetUIWindowBody/BuildBuildUIBody/AppendStatement 原样迁移而来，只是把
/// "拼字符串"换成了"调用 emitter"。新增元素类型时只需要改这一处 + <see cref="IPuiEmitter"/>
/// 的两个实现，不会有第三份"XML 元素是什么意思"的判断逻辑。
/// </summary>
internal static class PuiTreeWalker
{
    public static void Walk(PuiElement root, IPuiEmitter emitter)
    {
        emitter.CreateWindow(
            root.Name,
            root.PixelX,
            root.PixelY,
            root.Width,
            root.Height,
            root.AppearDir,
            root.AppearLen,
            root.Mask);

        if (root.FrameType != PuiFrameType.Main)
            emitter.SetFrameType(root.FrameType);
        if (root.Focusable)
            emitter.SetFocusable();

        IReadOnlyDictionary<string, string> buttonTriggers = CollectButtonTriggers(root);
        foreach (PuiElement child in root.Children)
            WalkChild(child, emitter, buttonTriggers);

        if (!string.IsNullOrEmpty(root.OnBuildCompleted))
            emitter.OnBuildCompleted(root.OnBuildCompleted);
    }

    /// <summary>root.StateTransitions 里 TriggerType==ButtonClick 的行，按 ButtonName 建一份
    /// "按钮名 -&gt; 触发 key"映射（此时两者相同，但走 ResolveTriggerKey() 保持跟其它触发类型
    /// 同一套取值口径，不在这里重复硬编码）；同一个按钮配了多条只取最后一条。</summary>
    private static IReadOnlyDictionary<string, string> CollectButtonTriggers(PuiElement root)
    {
        var map = new Dictionary<string, string>();
        foreach (PuiStateTransition t in root.StateTransitions)
        {
            if (t.TriggerType != PuiStateTriggerType.ButtonClick || string.IsNullOrEmpty(t.ButtonName))
                continue;
            map[t.ButtonName] = t.ResolveTriggerKey();
        }
        return map;
    }

    private static void WalkChild(PuiElement e, IPuiEmitter emitter, IReadOnlyDictionary<string, string> buttonTriggers)
    {
        switch (e.ElementType)
        {
            case PuiElementType.Text:
                emitter.AddText(new PuiTextParams
                {
                    Name = e.Name,
                    Text = e.Text,
                    Align = e.Align,
                    Width = e.Width,
                    Height = e.Height,
                    Html = e.Html,
                    Size = e.Size,
                    LineSpacing = e.LineSpacing,
                    LetterSpacing = e.LetterSpacing,
                    TextColor = PuiColor.Parse(e.Color, "FFFFFFFF"),
                    BackgroundColor = PuiColor.Parse(e.BackgroundColor, "00000000"),
                    BorderColor = PuiColor.Parse(e.BorderColor, "00000000"),
                });
                break;

            case PuiElementType.Button:
                emitter.AddButton(new PuiButtonParams
                {
                    Name = e.Name,
                    Title = e.Text,
                    Skin = e.Skin,
                    Width = e.Width,
                    Height = e.Height,
                    OnClick = e.OnClick,
                    TransitionTriggerKey = buttonTriggers.TryGetValue(e.Name, out string triggerKey) ? triggerKey : null,
                });
                break;

            case PuiElementType.Separator:
            {
                // 反编译真机 nel.UiBoxDesigner.Hr() / XX.Designer.addHr 确认：swidth<=0 时，真机会先自动
                // Br() 换到新的一行，再用 Designer.use_w（当前行剩余可用宽度）当占位宽度；"比例"参数在原版里
                // 从来都只喂给 draw_width_rate（绘制覆盖率），跟占多少布局宽度无关。这里不再自己拿
                // containerWidth * Ratio 猜一个像素值——那是之前对 Hr() 参数名的误读，且这个误读同时污染了
                // 生成的运行时代码。直接把 0 传给真机，让它按自己的规则决定宽度；编辑器预览
                // （PuiLineLayout.Compute）用同一套"整行独占"语义模拟，两边不会再各算一套。
                double pixelWidth = e.Vertical ? e.Height : 0;
                emitter.AddSeparator(new PuiSeparatorParams
                {
                    Width = pixelWidth,
                    Vertical = e.Vertical,
                    LineHeight = e.LineHeight,
                    MarginBefore = e.MarginBefore,
                    MarginAfter = e.MarginAfter,
                    DashedLength = e.DashedLength,
                    DrawWidthRate = e.DrawWidthRate,
                    Color = PuiColor.Parse(e.Color, "000000BE"),
                });
                break;
            }

            case PuiElementType.LineBreak:
                emitter.Br();
                break;

            case PuiElementType.LineStyle:
                emitter.SetLineAlign(e.LineAlign);
                break;

            case PuiElementType.DefaultLineStyle:
                emitter.SetDefaultLineAlign();
                break;

            case PuiElementType.ButtonMulti:
                emitter.AddButtonMulti(new PuiButtonMultiParams
                {
                    Name = e.Name,
                    Titles = SplitList(e.Titles),
                    Skin = e.Skin,
                    Width = e.Width,
                    Height = e.Height,
                    Columns = e.Columns,
                    MarginW = e.MarginW,
                    MarginH = e.MarginH,
                    NaviLoop = e.NaviLoop,
                    DefMask = e.DefMask,
                    LockedMask = e.LockedMask,
                    OnClick = e.OnClick,
                });
                break;

            case PuiElementType.Checks:
                emitter.AddChecks(new PuiChecksParams
                {
                    Name = e.Name,
                    Keys = SplitList(e.Keys),
                    Descs = SplitList(e.Descs),
                    Skin = e.Skin,
                    Width = e.Width,
                    Height = e.Height,
                    Scale = e.Scale,
                    Columns = e.Columns,
                    MarginW = RoundToInt(e.MarginW),
                    MarginH = RoundToInt(e.MarginH),
                    NaviLoop = e.NaviLoop,
                    DefMask = e.DefMask,
                    OnClick = e.OnClick,
                });
                break;

            case PuiElementType.Radio:
                emitter.AddRadio(new PuiRadioParams
                {
                    Name = e.Name,
                    Keys = SplitList(e.Keys),
                    Descs = SplitList(e.Descs),
                    Skin = e.Skin,
                    Width = e.Width,
                    Height = e.Height,
                    Columns = e.Columns,
                    Scale = e.Scale,
                    MarginW = RoundToInt(e.MarginW),
                    MarginH = RoundToInt(e.MarginH),
                    Def = RoundToInt(e.Def),
                    ValueReturnName = e.ValueReturnName,
                    AllFunctionSame = e.AllFunctionSame,
                    NaviLoop = e.NaviLoop,
                    RowMode = e.RowMode,
                    OnClick = e.OnClick,
                    OnChanged = e.OnChanged,
                });
                break;

            case PuiElementType.Slider:
                emitter.AddSlider(new PuiSliderParams
                {
                    Name = e.Name,
                    Title = e.Text,
                    Skin = e.Skin,
                    SkinTitle = e.SkinTitle,
                    Min = e.Min,
                    Max = e.Max,
                    Step = e.Step,
                    Width = e.Width,
                    Height = e.Height,
                    Def = e.Def,
                    SubmitHolding = e.SubmitHolding,
                    CheckboxMode = e.CheckboxMode,
                    DescKeys = SplitList(e.DescKeys),
                    SetterWidth = e.SetterWidth,
                    OnClick = e.OnClick,
                    OnChanged = e.OnChanged,
                });
                break;

            case PuiElementType.Input:
                emitter.AddInput(new PuiInputParams
                {
                    Name = e.Name,
                    Def = e.Text,
                    Label = e.Label,
                    Skin = e.Skin,
                    Width = e.Width,
                    BoundsWidth = e.BoundsWidth,
                    FontSize = e.FontSize,
                    Height = e.Height,
                    MaxLen = e.MaxLen,
                    Min = e.Min,
                    Max = e.Max,
                    Integer = e.Integer,
                    HexInteger = e.HexInteger,
                    Number = e.Number,
                    MultiLine = e.MultiLine,
                    LabelTop = e.LabelTop,
                    ReturnBlur = e.ReturnBlur,
                    Editable = e.Editable,
                    AllocEmpty = e.AllocEmpty,
                    ChangedDelayMaxT = e.ChangedDelayMaxT,
                    OnChanged = e.OnChanged,
                    OnChangedDelay = e.OnChangedDelay,
                });
                break;

            case PuiElementType.NumCounter:
                emitter.AddNumCounter(new PuiNumCounterParams
                {
                    Name = e.Name,
                    Def = RoundToInt(e.Def),
                    Locked = e.Locked,
                    Skin = e.Skin,
                    Width = e.Width,
                    Height = e.Height,
                    NaviLoop = e.NaviLoop,
                    MinVal = e.MinVal,
                    MaxVal = e.MaxVal,
                    Digit = e.Digit,
                    SlideCurDigitOnly = e.SlideCurDigitOnly,
                    OnClick = e.OnClick,
                });
                break;

            case PuiElementType.ColorCell:
                emitter.AddColorCell(new PuiColorCellParams
                {
                    Name = e.Name,
                    DefColor = PuiColor.Parse(e.DefColor, "FFFFFFFF"),
                    OpenPrompt = e.OpenPrompt,
                    UseText = e.UseText,
                    UseAlpha = e.UseAlpha,
                    Title = e.Text,
                    Skin = e.Skin,
                    SkinTitle = e.SkinTitle,
                    Width = e.Width,
                    Height = e.Height,
                    OnColorPromptDone = e.OnColorPromptDone,
                });
                break;

            case PuiElementType.Image:
                emitter.AddImage(new PuiImageParams
                {
                    Name = e.Name,
                    Width = e.Width,
                    Height = e.Height,
                    Scale = e.Scale,
                    StencilLessEqual = e.StencilLessEqual,
                    UvX = e.UvX,
                    UvY = e.UvY,
                    UvW = e.UvW,
                    UvH = e.UvH,
                    ImageSource = e.ImageSource,
                });
                break;
        }
    }

    /// <summary>把 ';' 分隔的字符串拆成数组；空输入返回 null（对应生成器里 SplitListLiteral 的 "null" 分支）。</summary>
    internal static string[] SplitList(string delimited)
    {
        if (string.IsNullOrWhiteSpace(delimited))
            return null;

        var items = new List<string>();
        foreach (string part in delimited.Split(';'))
        {
            string trimmed = part.Trim();
            if (trimmed.Length == 0) continue;
            items.Add(trimmed);
        }

        return items.Count == 0 ? null : items.ToArray();
    }

    // 只在 PuiElement 上是 double、而目标 nel 字段是 int 的少数几处使用（Checks/Radio 的
    // margin_w/h、Radio.def 索引、NumCounter.def）；其余字段两边都是 int，直接原样传递。
    private static int RoundToInt(double value) => (int)Math.Round(value);
}
