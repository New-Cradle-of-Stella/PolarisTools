using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using Polaris.Pevt.Commands;
using Polaris.Pevt.Diagnostics;
using Polaris.Pevt.Loading;
using Polaris.Pevt.Text;

namespace PolarisTools.Event.Pevt.Editor
{
    /// <summary>编辑器文本 → 共享 Core 的 <see cref="SourceText"/>。</summary>
    internal static class PevtEditorText
    {
        /// <summary>编辑缓冲区里的文件名只用于诊断展示，不参与解析。</summary>
        public const string BufferPath = "editor.pevt";

        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);

        /// <summary>
        /// 把编辑器文本交给 Core。
        ///
        /// Core 只接受 UTF-8 字节——"PEVT 永远以 UTF-8 原始源文本为真相"是全局不变量，连编辑器
        /// 预览也不例外，因此这里走的是和嵌入源、游戏侧完全相同的解码入口。
        /// 解码失败（编辑器给出了不可编码的内容）时返回 null，调用方按"这一轮不出结果"处理。
        /// </summary>
        public static SourceText? Load(string text, CancellationToken cancellationToken = default)
        {
            SourceTextLoadResult loaded = SourceText.FromUtf8(Utf8.GetBytes(text ?? string.Empty), BufferPath, cancellationToken);
            return loaded.Success ? loaded.Text : null;
        }
    }

    /// <summary>
    /// 一次实时诊断的结果，带着它算的是哪个文档版本。
    ///
    /// 版本号是"过期结果丢弃"的唯一依据：诊断在后台线程上跑，用户在这期间可能已经敲了三个字符，
    /// 那一轮结果必须被扔掉而不是画到新文本上。
    /// </summary>
    internal sealed class PevtDiagnosticsResult
    {
        public int Version { get; }

        public IReadOnlyList<Diagnostic> Diagnostics { get; }

        public PevtDiagnosticsResult(int version, IReadOnlyList<Diagnostic> diagnostics)
        {
            Version = version;
            Diagnostics = diagnostics;
        }

        public static PevtDiagnosticsResult Empty(int version) =>
            new PevtDiagnosticsResult(version, Array.Empty<Diagnostic>());
    }

    /// <summary>
    /// 实时诊断计算。刻意不引用任何 Visual Studio 类型——它是纯函数，可以脱离编辑器单独验证。
    ///
    /// 走的是 <see cref="PevtSourceCompiler.Compile(SourceText, BuiltinApiTable, CancellationToken, IEnumerable{Polaris.Pevt.Binding.Symbol}, Polaris.Pevt.Runtime.Raw.IPevtRawCsAnalyzer)"/>
    /// ——词法、语法、绑定、控制流四道静态门一次跑完，和游戏侧扫描嵌入源用的是同一个入口。
    /// 高亮那条路径只跑词法（见 <see cref="PevtTokenClassifier"/>），这一条必须跑完整，否则
    /// "工具侧和游戏侧对同一源码得到一致 PEVTxxxx"就不成立。
    /// </summary>
    /// <remarks>
    /// <c>$raw cs</c> 的 PEVT8007–8010 需要一个 C# 分析器。工具侧不带自己的 Roslyn（devenv.exe
    /// 已经有一份，VSIX 重复打包会造成同名程序集撞车），因此 <see cref="RawCsAnalyzer"/> 默认为
    /// null，那四个编号在编辑器里暂不产生；其余全部静态诊断不受影响。接上宿主 Roslyn 后即可生效，
    /// 而包装规则、类型映射与编号仍然来自共享 Core。
    /// </remarks>
    internal static class PevtLiveDiagnostics
    {
        /// <summary>可选的 <c>$raw cs</c> C# 分析器。宿主接上之后 PEVT8007–8010 在编辑器里生效。</summary>
        public static Polaris.Pevt.Runtime.Raw.IPevtRawCsAnalyzer? RawCsAnalyzer { get; set; }

        public static PevtDiagnosticsResult Analyze(string text, int version, CancellationToken cancellationToken)
        {
            SourceText? source = PevtEditorText.Load(text, cancellationToken);
            if (source == null)
                return PevtDiagnosticsResult.Empty(version);

            cancellationToken.ThrowIfCancellationRequested();

            PevtCompilation compilation = PevtSourceCompiler.Compile(
                source,
                CommandDescriptorCatalog.Builtin.ToBuiltinApiTable(),
                cancellationToken,
                null,
                RawCsAnalyzer);

            return new PevtDiagnosticsResult(version, compilation.Diagnostics);
        }
    }
}
