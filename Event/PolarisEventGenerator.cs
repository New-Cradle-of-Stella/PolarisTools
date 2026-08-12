using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Polaris.Event.Compiler;
using Polaris.Event.Compiler.Diagnostics;
using Polaris.Event.Compiler.Text;
using PolarisTools.Event.Language;

namespace PolarisTools.Event;

/// <summary>
/// .phxx -> C# 单文件生成器：接管原来挂在 Polaris.Event.Build（MSBuild Task + POLARIS_DIR 环境变量）
/// 上的构建期编译，改成和 .pui/.plang/.puisln 一样"保存时在编辑器里直接生成 .g.cs"的模式，消费方
/// 项目不再需要任何 MSBuild Import 或环境变量。引擎只有一份 <see cref="HppCompiler"/>，和实时诊断
/// （<see cref="HppDiagnosticsService"/>）共用，不允许另起一套，否则两条路径的报错会漂移。
/// </summary>
[ComVisible(true)]
[Guid("e9c7fcbe-5bcf-4b5d-930d-b40807a54c7f")]
[ClassInterface(ClassInterfaceType.None)]
public sealed class PolarisEventGenerator : IVsSingleFileGenerator
{
    public const string GeneratorName = "PolarisEventGenerator";

    public int DefaultExtension(out string pbstrDefaultExtension)
    {
        // Foo.phxx -> Foo.g.cs（VS 会先去掉 .phxx 再拼上这个扩展名）。
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
        ThreadHelper.ThrowIfNotOnUIThread();
        pcbOutput = 0;

        try
        {
            string logicalPath = ComputeLogicalPath(wszInputFilePath);
            var aliasFile = HppAliasFileLocator.FindAliasSource(Path.GetDirectoryName(wszInputFilePath));

            // 事件命名空间不再单独配置：直接复用宿主项目的默认命名空间（同时也是生成代码所在的
            // C# 命名空间），少一个要维护的 MSBuild 属性（PolarisEventNamespace 就此退休）。
            var project = new HppProject
            {
                Namespace = wszDefaultNamespace,
                RootNamespace = wszDefaultNamespace,
                Files = new[] { new SourceText(logicalPath, bstrInputFileContents) },
                AliasFile = aliasFile,
            };

            var result = new HppCompiler().Compile(project, CancellationToken.None);

            if (!result.Success)
            {
                ReportDiagnostics(result.Diagnostics, pGenerateProgress);
                return VSConstants.E_FAIL;
            }

            return WriteOutput(result.Files[0].GeneratedCSharp, rgbOutputFileContents, out pcbOutput);
        }
        catch (Exception ex)
        {
            pGenerateProgress?.GeneratorError(0, 0, ex.Message, 0, 0);
            return VSConstants.E_FAIL;
        }
    }

    static void ReportDiagnostics(IReadOnlyList<HppDiagnostic> diagnostics, IVsGeneratorProgress pGenerateProgress)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        foreach (var d in diagnostics)
        {
            if (d.Severity == DiagnosticSeverity.Info)
            {
                continue; // GeneratorError 只分 error/warning 两档，没有 info。
            }

            string message = d.Suggestion == null ? $"{d.Code}: {d.Message}" : $"{d.Code}: {d.Message} {d.Suggestion}";

            // HppDiagnostic.Span 的行列是 1-based（见 HxxParser.Span）。文档上 GeneratorError 的
            // dwLine/dwColumn 号称要 0-based，但实测 VS 错误列表是把这两个值原样当成显示的行列号，
            // 减 1 传进去只会让错误列表里的行号比脚本里的实际行号少 1——所以这里不做转换。
            pGenerateProgress?.GeneratorError(
                d.Severity == DiagnosticSeverity.Warning ? 1 : 0,
                0,
                message,
                (uint)Math.Max(0, d.Span.Line),
                (uint)Math.Max(0, d.Span.Column));
        }
    }

    static int WriteOutput(string generatedCSharp, IntPtr[] rgbOutputFileContents, out uint pcbOutput)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(generatedCSharp);
        IntPtr outputBuffer = Marshal.AllocCoTaskMem(bytes.Length);
        Marshal.Copy(bytes, 0, outputBuffer, bytes.Length);
        rgbOutputFileContents[0] = outputBuffer;
        pcbOutput = (uint)bytes.Length;
        return VSConstants.S_OK;
    }

    /// <summary>
    /// 事件的运行时身份（生成代码里 <c>PolarisEventReference</c> 的 logicalId，也是
    /// <c>GeneratedEvents</c> 上那个静态成员的名字）必须是相对于所在项目的路径，不能是开发机上的
    /// 绝对路径——否则不同机器/不同 clone 目录编译出来的 mod 里事件 ID 就对不上了，生成的类名也会
    /// 变成一串盘符和用户名。单文件生成器拿到的只有绝对路径，这里从输入文件所在目录向上找最近的
    /// .csproj 自己算相对路径（和已废弃的 PolarisEventCompileTask.MakeRelative 是同一套算法，
    /// 保证 logicalId 的形状不因为这次迁移而变化）；找不到就退化成裸文件名。
    /// </summary>
    static string ComputeLogicalPath(string filePath)
    {
        string? projectDir = FindContainingProjectDirectory(filePath);
        if (projectDir == null)
        {
            return Path.GetFileName(filePath);
        }

        string baseDir = projectDir.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
            ? projectDir
            : projectDir + Path.DirectorySeparatorChar;

        var baseUri = new Uri(baseDir);
        var fileUri = new Uri(filePath);
        return Uri.UnescapeDataString(baseUri.MakeRelativeUri(fileUri).ToString());
    }

    static string? FindContainingProjectDirectory(string startFilePath)
    {
        string? dir = Path.GetDirectoryName(startFilePath);
        for (int i = 0; dir != null && i < 8; i++)
        {
            if (Directory.EnumerateFiles(dir, "*.csproj").Any())
            {
                return dir;
            }

            dir = Path.GetDirectoryName(dir);
        }

        return null;
    }
}
