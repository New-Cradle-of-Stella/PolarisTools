using Polaris.PUI.Wire;
using System.Collections.Generic;
using System.IO;

namespace PolarisTools.Pui.PuiVisualEditor.HotReload;

/// <summary>
/// 把一份 <see cref="PuiWireCommand"/> 列表写成字节流，通过命名管道发给游戏进程。
/// 字段顺序、类型必须跟 Polaris 里的 PuiWireReader（PUI/HotReload/PuiWireReader.cs）
/// 逐条对应——新增/调整某个 Add* 的字段时两边都要改，且顺序要完全一致。
/// 字符串一律按"" 处理 null（跟 codegen/interpreter 里 IsNullOrEmpty 判空的语义一致，
/// 不需要额外的 has-value 标记）；字符串数组用一个 bool 标记是否为 null。
/// </summary>
internal static class PuiWireWriter
{
    public static void Write(BinaryWriter w, string puiName, IReadOnlyList<PuiWireCommand> commands)
    {
        // 版本号必须是帧里的第一个字段：读端要在按任何字段布局解析之前就能判断版本，
        // 否则版本不匹配时会拿错误的字节序列去填载荷，表现为难以定位的乱数据而不是明确报错。
        w.Write(PuiProtocol.Version);
        w.Write(puiName ?? "");
        w.Write(commands.Count);
        foreach (PuiWireCommand cmd in commands)
        {
            w.Write((int)cmd.Opcode);
            switch (cmd.Opcode)
            {
                case PuiWireOpcode.CreateWindow:
                {
                    var p = (PuiCreateWindowParams)cmd.Payload;
                    w.Write(p.Name ?? "");
                    w.Write(p.PixelX);
                    w.Write(p.PixelY);
                    w.Write(p.Width);
                    w.Write(p.Height);
                    w.Write(p.AppearDir);
                    w.Write(p.AppearLen);
                    w.Write((int)p.Mask);
                    break;
                }

                case PuiWireOpcode.SetFrameType:
                    w.Write((int)((PuiFrameTypeParams)cmd.Payload).FrameType);
                    break;

                case PuiWireOpcode.SetFocusable:
                case PuiWireOpcode.Br:
                case PuiWireOpcode.SetDefaultLineAlign:
                    break;

                case PuiWireOpcode.AddText:
                {
                    var p = (PuiTextParams)cmd.Payload;
                    w.Write(p.Name ?? "");
                    w.Write(p.Text ?? "");
                    w.Write((int)p.Align);
                    w.Write(p.Width);
                    w.Write(p.Height);
                    w.Write(p.Html);
                    w.Write(p.Size);
                    w.Write(p.LineSpacing);
                    w.Write(p.LetterSpacing);
                    WriteColor(w, p.TextColor);
                    WriteColor(w, p.BackgroundColor);
                    WriteColor(w, p.BorderColor);
                    break;
                }

                case PuiWireOpcode.AddButton:
                {
                    var p = (PuiButtonParams)cmd.Payload;
                    w.Write(p.Name ?? "");
                    w.Write(p.Title ?? "");
                    w.Write(p.Skin ?? "");
                    w.Write(p.Width);
                    w.Write(p.Height);
                    w.Write(p.OnClick ?? "");
                    w.Write(p.TransitionTriggerKey ?? "");
                    break;
                }

                case PuiWireOpcode.AddSeparator:
                {
                    var p = (PuiSeparatorParams)cmd.Payload;
                    w.Write(p.Width);
                    w.Write(p.Vertical);
                    w.Write(p.LineHeight);
                    w.Write(p.MarginBefore);
                    w.Write(p.MarginAfter);
                    w.Write(p.DashedLength);
                    w.Write(p.DrawWidthRate);
                    WriteColor(w, p.Color);
                    break;
                }

                case PuiWireOpcode.SetLineAlign:
                    w.Write((int)((PuiLineAlignParams)cmd.Payload).Align);
                    break;

                case PuiWireOpcode.AddButtonMulti:
                {
                    var p = (PuiButtonMultiParams)cmd.Payload;
                    w.Write(p.Name ?? "");
                    WriteStringArray(w, p.Titles);
                    w.Write(p.Skin ?? "");
                    w.Write(p.Width);
                    w.Write(p.Height);
                    w.Write(p.Columns);
                    w.Write(p.MarginW);
                    w.Write(p.MarginH);
                    w.Write(p.NaviLoop);
                    w.Write(p.DefMask);
                    w.Write(p.LockedMask);
                    w.Write(p.OnClick ?? "");
                    break;
                }

                case PuiWireOpcode.AddChecks:
                {
                    var p = (PuiChecksParams)cmd.Payload;
                    w.Write(p.Name ?? "");
                    WriteStringArray(w, p.Keys);
                    WriteStringArray(w, p.Descs);
                    w.Write(p.Skin ?? "");
                    w.Write(p.Width);
                    w.Write(p.Height);
                    w.Write(p.Scale);
                    w.Write(p.Columns);
                    w.Write(p.MarginW);
                    w.Write(p.MarginH);
                    w.Write(p.NaviLoop);
                    w.Write(p.DefMask);
                    w.Write(p.OnClick ?? "");
                    break;
                }

                case PuiWireOpcode.AddRadio:
                {
                    var p = (PuiRadioParams)cmd.Payload;
                    w.Write(p.Name ?? "");
                    WriteStringArray(w, p.Keys);
                    WriteStringArray(w, p.Descs);
                    w.Write(p.Skin ?? "");
                    w.Write(p.Width);
                    w.Write(p.Height);
                    w.Write(p.Columns);
                    w.Write(p.Scale);
                    w.Write(p.MarginW);
                    w.Write(p.MarginH);
                    w.Write(p.Def);
                    w.Write(p.ValueReturnName);
                    w.Write(p.AllFunctionSame);
                    w.Write(p.NaviLoop);
                    w.Write(p.RowMode);
                    w.Write(p.OnClick ?? "");
                    w.Write(p.OnChanged ?? "");
                    break;
                }

                case PuiWireOpcode.AddSlider:
                {
                    var p = (PuiSliderParams)cmd.Payload;
                    w.Write(p.Name ?? "");
                    w.Write(p.Title ?? "");
                    w.Write(p.Skin ?? "");
                    w.Write(p.SkinTitle ?? "");
                    w.Write(p.Min);
                    w.Write(p.Max);
                    w.Write(p.Step);
                    w.Write(p.Width);
                    w.Write(p.Height);
                    w.Write(p.Def);
                    w.Write(p.SubmitHolding);
                    w.Write(p.CheckboxMode);
                    WriteStringArray(w, p.DescKeys);
                    w.Write(p.SetterWidth);
                    w.Write(p.OnClick ?? "");
                    w.Write(p.OnChanged ?? "");
                    break;
                }

                case PuiWireOpcode.AddInput:
                {
                    var p = (PuiInputParams)cmd.Payload;
                    w.Write(p.Name ?? "");
                    w.Write(p.Def ?? "");
                    w.Write(p.Label ?? "");
                    w.Write(p.Skin ?? "");
                    w.Write(p.Width);
                    w.Write(p.BoundsWidth);
                    w.Write(p.FontSize);
                    w.Write(p.Height);
                    w.Write(p.MaxLen);
                    w.Write(p.Min);
                    w.Write(p.Max);
                    w.Write(p.Integer);
                    w.Write(p.HexInteger);
                    w.Write(p.Number);
                    w.Write(p.MultiLine);
                    w.Write(p.LabelTop);
                    w.Write(p.ReturnBlur);
                    w.Write(p.Editable);
                    w.Write(p.AllocEmpty);
                    w.Write(p.ChangedDelayMaxT);
                    w.Write(p.OnChanged ?? "");
                    w.Write(p.OnChangedDelay ?? "");
                    break;
                }

                case PuiWireOpcode.AddNumCounter:
                {
                    var p = (PuiNumCounterParams)cmd.Payload;
                    w.Write(p.Name ?? "");
                    w.Write(p.Def);
                    w.Write(p.Locked);
                    w.Write(p.Skin ?? "");
                    w.Write(p.Width);
                    w.Write(p.Height);
                    w.Write(p.NaviLoop);
                    w.Write(p.MinVal);
                    w.Write(p.MaxVal);
                    w.Write(p.Digit);
                    w.Write(p.SlideCurDigitOnly);
                    w.Write(p.OnClick ?? "");
                    break;
                }

                case PuiWireOpcode.AddColorCell:
                {
                    var p = (PuiColorCellParams)cmd.Payload;
                    w.Write(p.Name ?? "");
                    WriteColor(w, p.DefColor);
                    w.Write(p.OpenPrompt);
                    w.Write(p.UseText);
                    w.Write(p.UseAlpha);
                    w.Write(p.Title ?? "");
                    w.Write(p.Skin ?? "");
                    w.Write(p.SkinTitle ?? "");
                    w.Write(p.Width);
                    w.Write(p.Height);
                    w.Write(p.OnColorPromptDone ?? "");
                    break;
                }

                case PuiWireOpcode.AddImage:
                {
                    var p = (PuiImageParams)cmd.Payload;
                    w.Write(p.Name ?? "");
                    w.Write(p.Width);
                    w.Write(p.Height);
                    w.Write(p.Scale);
                    w.Write(p.StencilLessEqual);
                    w.Write(p.UvX);
                    w.Write(p.UvY);
                    w.Write(p.UvW);
                    w.Write(p.UvH);
                    w.Write(p.ImageSource ?? "");
                    break;
                }

                case PuiWireOpcode.OnBuildCompleted:
                    w.Write(((PuiMethodNameParams)cmd.Payload).MethodName ?? "");
                    break;
            }
        }
    }

    private static void WriteColor(BinaryWriter w, PuiColor c)
    {
        w.Write(c.R);
        w.Write(c.G);
        w.Write(c.B);
        w.Write(c.A);
    }

    private static void WriteStringArray(BinaryWriter w, string[] items)
    {
        w.Write(items != null);
        if (items == null) return;
        w.Write(items.Length);
        foreach (string s in items)
            w.Write(s ?? "");
    }
}
