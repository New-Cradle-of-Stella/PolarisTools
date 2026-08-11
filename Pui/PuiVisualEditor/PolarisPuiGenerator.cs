using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Xml.Linq;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace PolarisTools.Pui.PuiVisualEditor;

[ComVisible(true)]
[Guid(PolarisToolsPackage.PackageGuidString)]
[ClassInterface(ClassInterfaceType.None)]
public sealed class PolarisPuiGenerator : IVsSingleFileGenerator
{
    public const string GeneratorName = "PolarisPuiGenerator";

    public int DefaultExtension(out string pbstrDefaultExtension)
    {
        // Test.pui -> Test.g.cs（VS 会先去掉 .pui 再拼上这个扩展名，不是 Test.pui.g.cs）
        pbstrDefaultExtension = ".g.cs";
        return VSConstants.S_OK;
    }

    public int Generate(
        string wszInputFilePath,
        string bstrInputFileContents,
        string wszDefaultNamespace,
        IntPtr[] rgbOutputFileContents,
        out uint pcbOutput,
        IVsGeneratorProgress pGenerateProgress)
    {
        pcbOutput = 0;

        try
        {
            string generatedCode = GenerateCSharp(
                wszInputFilePath,
                bstrInputFileContents,
                wszDefaultNamespace);

            byte[] bytes = Encoding.UTF8.GetBytes(generatedCode);

            IntPtr outputBuffer = Marshal.AllocCoTaskMem(bytes.Length);
            Marshal.Copy(bytes, 0, outputBuffer, bytes.Length);

            rgbOutputFileContents[0] = outputBuffer;
            pcbOutput = (uint)bytes.Length;

            return VSConstants.S_OK;
        }
        catch (Exception ex)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            pGenerateProgress?.GeneratorError(
                0,
                0,
                ex.Message,
                0,
                0);
            return VSConstants.E_FAIL;
        }
    }

    internal static string ComputeClassName(string inputFilePath)
        => Path.GetFileNameWithoutExtension(inputFilePath).ToUpper();

    internal static string ResolveNamespace(string defaultNamespace)
        => string.IsNullOrWhiteSpace(defaultNamespace) ? "Polaris.Generated" : defaultNamespace;

    /// <summary>
    /// 把 .pui 的 XML 内容解析成 PuiElement 树；内容为空或暂时不是合法 XML
    /// （比如刚新建、还没保存过一次）时回退成一个空面板，不让生成过程整体失败。
    /// </summary>
    internal static PuiElement ParseRoot(string xml, string fallbackName)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(xml))
            {
                XElement doc = XElement.Parse(xml);
                PuiElement parsed = PuiElement.FromXml(doc);
                if (parsed != null)
                    return parsed;
            }
        }
        catch
        {
            // 忽略：回退到下面的默认空面板。
        }

        return new PuiElement(PuiElementType.Window) { Name = fallbackName };
    }

    // 回调签名表（照抄 DsnData 技术文档 §3.4）。CollectRequiredHandlers（按已填的方法名反查）和
    // GetHandlerSignature（按 (元素类型, hook 种类) 正查）两条路径必须给出完全相同的签名，
    // 所以两边都只引用这里的常量，不再各自写一遍字符串字面量。
    private const string ClickSig = "XX.aBtn _B";
    private const string RadioChangedSig = "XX.BtnContainerRadio<XX.aBtn> container, int previous, int current";
    private const string SliderChangedSig = "XX.aBtnMeter button, float previous, float current";
    private const string InputChangedSig = "XX.LabeledInputField field";
    private const string ColorChangedSig = "XX.aBtnColorCell button, UnityEngine.Color32 previous, UnityEngine.Color32 current";
    private const string BuildCompletedSig = "UiBoxDesigner designer";
    private const string ReturnFalse = "return false;";

    /// <summary>
    /// 一个需要在 .pui.cs 里存在的回调/配置方法桩：方法名、返回类型、参数列表、缺省方法体。
    /// 签名照抄 DsnData 技术文档 §3.4 的回调签名表；OnBuildCompleted 返回 void，其余返回 bool。
    /// </summary>
    internal sealed class HandlerRequirement
    {
        public string MethodName;
        public string ReturnType;
        public string Parameters;
        public string DefaultBody;
    }

    /// <summary>
    /// 收集这个 .pui 里所有 OnClick/OnChanged/OnChangedDelay/OnColorChanged
    /// 属性指向的方法名及其应有签名，供 package 侧检查 .pui.cs 里是否已有对应的桩方法。
    /// </summary>
    internal static IReadOnlyList<HandlerRequirement> CollectRequiredHandlers(string inputFileContents)
    {
        var result = new List<HandlerRequirement>();
        PuiElement root = ParseRoot(inputFileContents, "Root");

        void Add(string methodName, string returnType, string parameters, string defaultBody)
        {
            if (string.IsNullOrEmpty(methodName)) return;
            result.Add(new HandlerRequirement
            {
                MethodName = methodName,
                ReturnType = returnType,
                Parameters = parameters,
                DefaultBody = defaultBody,
            });
        }

        void Walk(PuiElement e)
        {
            switch (e.ElementType)
            {
                case PuiElementType.Window:
                    Add(e.OnBuildCompleted, "void", BuildCompletedSig, "");
                    break;
                case PuiElementType.Button:
                case PuiElementType.ButtonMulti:
                case PuiElementType.Checks:
                case PuiElementType.NumCounter:
                    Add(e.OnClick, "bool", ClickSig, ReturnFalse);
                    break;
                case PuiElementType.Radio:
                    Add(e.OnClick, "bool", ClickSig, ReturnFalse);
                    Add(e.OnChanged, "bool", RadioChangedSig, ReturnFalse);
                    break;
                case PuiElementType.Slider:
                    Add(e.OnClick, "bool", ClickSig, ReturnFalse);
                    Add(e.OnChanged, "bool", SliderChangedSig, ReturnFalse);
                    break;
                case PuiElementType.Input:
                    Add(e.OnChanged, "bool", InputChangedSig, ReturnFalse);
                    Add(e.OnChangedDelay, "bool", InputChangedSig, ReturnFalse);
                    break;
                case PuiElementType.ColorCell:
                    Add(e.OnColorPromptDone, "bool", ColorChangedSig, ReturnFalse);
                    break;
            }
            foreach (PuiElement child in e.Children)
                Walk(child);
        }

        Walk(root);
        return result;
    }

    /// <summary>
    /// 给定元素类型和 hook 种类（"OnClick"/"OnChanged"/"OnChangedDelay"/"OnColorChanged"/"OnBuildCompleted"），
    /// 返回它应有的方法签名（不含方法名，调用方自行填），跟 <see cref="CollectRequiredHandlers"/>
    /// 用的是同一套签名表。用于可视化编辑器"一键创建回调"按钮：那时属性可能还是空的，
    /// 没法像 CollectRequiredHandlers 那样从已填的方法名反查，所以单独按 (类型, hook 种类) 查表。
    /// 该类型不支持这种 hook 时返回 null。
    /// </summary>
    internal static HandlerRequirement GetHandlerSignature(PuiElementType elementType, string hookKind)
    {
        switch (hookKind)
        {
            case "OnClick":
                switch (elementType)
                {
                    case PuiElementType.Button:
                    case PuiElementType.ButtonMulti:
                    case PuiElementType.Checks:
                    case PuiElementType.Radio:
                    case PuiElementType.Slider:
                    case PuiElementType.NumCounter:
                        return BoolHandler(ClickSig);
                    default:
                        return null;
                }

            case "OnChanged":
                return elementType switch
                {
                    PuiElementType.Radio => BoolHandler(RadioChangedSig),
                    PuiElementType.Slider => BoolHandler(SliderChangedSig),
                    PuiElementType.Input => BoolHandler(InputChangedSig),
                    _ => null,
                };

            case "OnChangedDelay":
                return elementType == PuiElementType.Input ? BoolHandler(InputChangedSig) : null;

            case "OnColorChanged":
                return elementType == PuiElementType.ColorCell ? BoolHandler(ColorChangedSig) : null;

            case "OnBuildCompleted":
                return elementType == PuiElementType.Window
                    ? new HandlerRequirement { ReturnType = "void", Parameters = BuildCompletedSig, DefaultBody = "" }
                    : null;

            default:
                return null;
        }
    }

    private static HandlerRequirement BoolHandler(string parameters) =>
        new HandlerRequirement { ReturnType = "bool", Parameters = parameters, DefaultBody = ReturnFalse };

    private static string GenerateCSharp(
        string inputFilePath,
        string inputFileContents,
        string defaultNamespace)
    {
        string name = Path.GetFileNameWithoutExtension(inputFilePath);
        string className = ComputeClassName(inputFilePath);
        string ns = ResolveNamespace(defaultNamespace);

        PuiElement root = ParseRoot(inputFileContents, name);
        // Window.Name 已经不在属性面板里编辑、也不落盘了（见 PuiElement.ToXml），生成时统一
        // 强制成文件名本身，不管 .pui 文件里当年是不是还留着旧版本手填的 Name 属性。
        root.Name = name;

        var emitter = new CSharpTextEmitter();
        PuiTreeWalker.Walk(root, emitter);
        string getUIWindowBody = emitter.GetUIWindowBody;
        string buildUIBody = emitter.BuildUIBody();
        string extraMembers = emitter.ExtraMembers;

        // 这个类是完全自动生成的，用户不需要看也不需要改。
        // 它和 {{name}}.pui.cs 里的同名 partial class 拼在一起；交互逻辑写在那边。
        return $$"""
            {{Header}}

            namespace {{ns}};

            [PUIAutoRegistration]
            public partial class {{className}} : IPUI
            {
                public string Name { get => "{{Esc(name)}}"; }

                public UiBoxDesigner GetUIWindow(UiBoxDesignerFamily source)
                {
            {{getUIWindowBody}}
                }

                public void BuildUI(UiBoxDesigner designer)
                {
            {{buildUIBody}}
                }{{extraMembers}}
            }
            """;
    }

    private static string Esc(string value) => CSharpLiteral.Escape(value);

    /// <summary>
    /// 生成 .pui.cs 的初始骨架（仅在文件不存在时使用一次，之后不会被覆盖；
    /// 缺失的回调/配置方法桩由 package 侧按需追加，见 PolarisToolsPackage）。
    /// </summary>
    internal static string GenerateCodeBehindSkeleton(string inputFilePath, string defaultNamespace)
    {
        string name = Path.GetFileNameWithoutExtension(inputFilePath);
        string className = ComputeClassName(inputFilePath);
        string ns = ResolveNamespace(defaultNamespace);

        return $$"""
            // {{name}}.pui 的代码文件。
            // 在 {{name}}.pui 里把界面搭好之后，按钮点击等交互逻辑写在这里。
            // 这个文件只会创建一次，以后保存 {{name}}.pui 也不会覆盖它，可以放心修改。
            // 新增的 OnClick/OnChanged/OnColorChanged 方法桩会自动追加到这个文件末尾，已有方法不会被改动。

            using Polaris;
            using Polaris.PUI;
            using nel;
            using XX;

            namespace {{ns}};

            public partial class {{className}}
            {

            }
            """;
    }

    const string Header = $$"""
        // <auto-generated />
        // Generated by polaris source code generator
        // Report any problem to xxxx

        using System;
        using Polaris;
        using Polaris.PUI;
        using nel;
        using XX;
        """;
}
