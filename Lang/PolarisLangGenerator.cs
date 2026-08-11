using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Polaris.Lang;
using PolarisTools.Pui;

namespace PolarisTools.Lang;

/// <summary>
/// .plang → C# 单文件生成器。文件里的每个 Key 生成两样东西：
/// <list type="bullet">
/// <item>一个只读静态属性，实时调用 <c>Polaris.Lang.PlangRuntime.Get(key)</c>——按玩家当前
/// 游戏语言取词，未命中语言退回中性值。不缓存文案，语言切换后下次读取立即生效。</item>
/// <item>一个标了 <c>[PlangAutoRegistration]</c>、实现 <c>IPlangRegistrar</c> 的注册类，
/// 把这份文件的 Key/中性值/各启用语言的文案交给 <c>PlangRuntime.Register</c>——
/// <c>PolarisLang</c> 的 <c>PlangRegistryScanner</c> 会在插件 Init 阶段扫到并调用它，取代旧版
/// 运行时扫 <c>.plang</c> 文件的做法，发布包里也就不再需要带 <c>.plang</c> 数据文件了。</item>
/// </list>
/// <para>
/// 生成的类是纯自动内容、不需要用户手写交互逻辑，所以不像 .pui 那样再生成一份
/// code-behind 骨架——和 .puisln 的 <c>_Solution</c> 类同一个做法。只读属性所在的类保持
/// <c>internal</c>（一个 .plang 只服务于它所在的这个程序集）；注册类必须 <c>public</c>，
/// 因为 <c>PlangRegistryScanner</c> 靠 <c>Activator.CreateInstance</c> 构造它，只能调公开的
/// 无参构造函数。
/// </para>
/// </summary>
// 独立 GUID：不能和 PolarisPuiGenerator/PolarisPuislnGenerator 共用，见后两者代码里的同类说明。
[ComVisible(true)]
[Guid("6f1a2b3c-8d4e-4f5a-9b6c-7d8e9f0a1b2c")]
[ClassInterface(ClassInterfaceType.None)]
public sealed class PolarisLangGenerator : IVsSingleFileGenerator
{
    public const string GeneratorName = "PolarisLangGenerator";

    public int DefaultExtension(out string pbstrDefaultExtension)
    {
        // Foo.plang -> Foo.g.cs（VS 会先去掉 .plang 再拼上这个扩展名）。
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
        // VS 保证单文件生成器在主线程上调用；显式断言而不是隐式依赖这个前提。
        ThreadHelper.ThrowIfNotOnUIThread();
        pcbOutput = 0;

        try
        {
            string generatedCode = GenerateCSharp(wszInputFilePath, bstrInputFileContents, wszDefaultNamespace);

            byte[] bytes = Encoding.UTF8.GetBytes(generatedCode);
            IntPtr outputBuffer = Marshal.AllocCoTaskMem(bytes.Length);
            Marshal.Copy(bytes, 0, outputBuffer, bytes.Length);

            rgbOutputFileContents[0] = outputBuffer;
            pcbOutput = (uint)bytes.Length;
            return VSConstants.S_OK;
        }
        catch (Exception ex)
        {
            pGenerateProgress?.GeneratorError(0, 0, ex.Message, 0, 0);
            return VSConstants.E_FAIL;
        }
    }

    internal static string ComputeClassName(string inputFilePath)
        => CSharpLiteral.SanitizeIdentifier(Path.GetFileNameWithoutExtension(inputFilePath));

    internal static string ResolveNamespace(string defaultNamespace)
        => string.IsNullOrWhiteSpace(defaultNamespace) ? "Polaris.Generated" : defaultNamespace;

    /// <summary>
    /// <see cref="CSharpLiteral.Escape"/> 只处理反斜杠/双引号，够用于 .pui 那边的标识符类
    /// 字面量。这里的文案是用户自由文本，Long 类型经常带真换行——不转义会生成非法的单行
    /// 字符串字面量，编译直接失败，所以本地额外转一遍 <c>\r</c>/<c>\n</c>，不改共用的
    /// <see cref="CSharpLiteral"/>（别的调用方目前用不到，也没必要跟着改行为）。
    /// </summary>
    static string EscapeLiteral(string value) =>
        CSharpLiteral.Escape(value).Replace("\r", "\\r").Replace("\n", "\\n");

    private static string GenerateCSharp(string inputFilePath, string inputFileContents, string defaultNamespace)
    {
        string className = ComputeClassName(inputFilePath);
        string ns = ResolveNamespace(defaultNamespace);

        PlangDocument doc;
        try
        {
            doc = PlangDocument.Parse(inputFileContents);
        }
        catch (Exception ex)
        {
            // 内容为空或暂时不是合法 XML（刚新建、还没保存过一次）时退回一个空类，
            // 不让整个生成过程失败——和 PolarisPuiGenerator.ParseRoot 的容错策略一致。
            System.Diagnostics.Debug.WriteLine($"Polaris：解析 {inputFilePath} 失败，生成空类：{ex.Message}");
            doc = new PlangDocument();
        }

        var enabledCodes = new HashSet<string>(
            doc.Languages.Where(l => l.Enabled && !string.IsNullOrEmpty(l.Code)).Select(l => l.Code),
            StringComparer.OrdinalIgnoreCase);

        var members = new StringBuilder();
        var registrations = new StringBuilder();
        var usedIdentifiers = new HashSet<string>(StringComparer.Ordinal);

        foreach (PlangEntry entry in doc.Entries)
        {
            if (string.IsNullOrEmpty(entry.Key))
                continue;

            string identifier = CSharpLiteral.SanitizeIdentifier(entry.Key);
            if (!usedIdentifiers.Add(identifier))
                continue; // Key 净化后撞名，保留先出现的那个，避免生成的类编译失败。

            string comment = string.IsNullOrEmpty(entry.Comment) ? "" : $"    /// <summary>{System.Security.SecurityElement.Escape(entry.Comment)}</summary>\n";
            members.Append(comment)
                .Append("    public static string ").Append(identifier)
                .Append(" => global::Polaris.Lang.PlangRuntime.Get(\"")
                .Append(CSharpLiteral.Escape(entry.Key)).Append("\");\n");

            registrations.Append("        global::Polaris.Lang.PlangRuntime.Register(\"")
                .Append(CSharpLiteral.Escape(entry.Key)).Append("\", \"")
                .Append(EscapeLiteral(entry.NeutralValue)).Append("\",\n")
                .Append("            new global::System.Collections.Generic.Dictionary<string, string>\n")
                .Append("            {\n");

            foreach (KeyValuePair<string, string> kv in entry.Values)
            {
                if (!enabledCodes.Contains(kv.Key))
                    continue; // 禁用的语言列不参与生成/注册，数据仍然留在 .plang 文件里。

                registrations.Append("                [\"").Append(CSharpLiteral.Escape(kv.Key)).Append("\"] = \"")
                    .Append(EscapeLiteral(kv.Value)).Append("\",\n");
            }

            registrations.Append("            });\n");
        }

        return $$"""
            // <auto-generated />
            // Generated by polaris source code generator from {{Path.GetFileName(inputFilePath)}}

            namespace {{ns}}
            {
                internal static class {{className}}
                {
            {{members}}    }

                [global::Polaris.Lang.PlangAutoRegistration]
                public sealed class {{className}}_PlangRegistrar : global::Polaris.Lang.IPlangRegistrar
                {
                    public void Register()
                    {
            {{registrations}}    }
                }
            }
            """;
    }
}
