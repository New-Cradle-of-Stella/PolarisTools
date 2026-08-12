using Polaris.Event.Compiler;
using Polaris.Event.Compiler.Diagnostics;
using Polaris.Event.Compiler.Text;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace PolarisTools.Event.Language
{
    /// <summary>
    /// 给编辑器实时诊断用的最小 <see cref="HppProject"/> 拼装：把当前缓冲区文本和它所在目录下的
    /// 其它 <c>.phxx</c>/别名文件一起丢给 <see cref="HppCompiler.Analyze"/>，只取回当前文件的诊断。
    /// 不做实现计划 §6.3 描述的整项目缓存/跨项目事件 ID 索引——那是阶段5+的作者体验打磨；
    /// MVP 只要"改了就在 300ms 左右挑出这一份文件里的错"。
    /// </summary>
    internal static class HppDiagnosticsService
    {
        static readonly HppCompiler Compiler = new HppCompiler();

        public static HppEditorAnalysisResult AnalyzeFile(string filePath, string text, CancellationToken token)
        {
            string directory = string.IsNullOrEmpty(filePath) ? null : Path.GetDirectoryName(filePath);
            var aliasFile = HppAliasFileLocator.FindAliasSource(directory);
            string aliasDirectory = aliasFile != null ? Path.GetDirectoryName(aliasFile.Path) : null;

            var project = new HppProject
            {
                Namespace = "editor.preview",
                Files = new[] { new SourceText(filePath ?? "<unsaved>.phxx", text) },
                AliasFile = aliasFile,
            };

            try
            {
                var analysis = Compiler.Analyze(project, token);
                string key = filePath ?? "<unsaved>.phxx";
                var diagnostics = analysis.DiagnosticsByFile.TryGetValue(key, out var d) ? d : Array.Empty<HppDiagnostic>();
                return new HppEditorAnalysisResult(diagnostics, directory, aliasDirectory, aliasFile?.Path);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                var crash = new[]
                {
                    new HppDiagnostic(
                        DiagnosticCodes.InternalError,
                        DiagnosticSeverity.Error,
                        $"Editor analysis crashed: {ex.Message}",
                        new SourceSpan(filePath ?? "<unsaved>.phxx", 1, 1)),
                };
                return new HppEditorAnalysisResult(crash, directory, aliasDirectory, aliasFile?.Path);
            }
        }

    }

    /// <summary>一次编辑器分析的结果：诊断本身 + 触发重新分析所需要监视的两个目录（文件自身所在目录、
    /// 实际用到的 alias 文件所在目录，后者可能因为向上查找而和前者不同）。</summary>
    internal sealed class HppEditorAnalysisResult
    {
        public IReadOnlyList<HppDiagnostic> Diagnostics { get; }
        public string FileDirectory { get; }
        public string AliasDirectory { get; }
        public string AliasFilePath { get; }

        public HppEditorAnalysisResult(IReadOnlyList<HppDiagnostic> diagnostics, string fileDirectory, string aliasDirectory, string aliasFilePath)
        {
            Diagnostics = diagnostics;
            FileDirectory = fileDirectory;
            AliasDirectory = aliasDirectory;
            AliasFilePath = aliasFilePath;
        }
    }
}
