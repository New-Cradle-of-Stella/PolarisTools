using EnvDTE;
using Microsoft.VisualStudio.Shell;
using Polaris.Event.Compiler;
using Polaris.Event.Compiler.Text;
using System;
using System.ComponentModel.Design;
using System.IO;
using System.Threading;
using Task = System.Threading.Tasks.Task;

namespace PolarisTools.Event.Language
{
    /// <summary>
    /// 阶段5 §8 的 "hppc explain"：把当前打开的 .phxx 编译一遍，把生成的底层 CMD（或诊断，如果编译失败）
    /// 打到输出窗口。走 <see cref="EnvDTE.TextDocument"/> 拿编辑器里的最新文本（含未保存的改动），
    /// 跟 <see cref="HppDiagnosticTagger"/> 用的是同一个 <see cref="HppCompiler"/>。
    /// </summary>
    internal sealed class HppExplainCommand
    {
        public const int CommandId = 0x0103;
        public static readonly Guid CommandSet = new Guid("1ba8fc7a-877c-43a5-8937-e1ed1b2dacea");

        readonly AsyncPackage package;

        HppExplainCommand(AsyncPackage package, OleMenuCommandService commandService)
        {
            this.package = package;
            commandService.AddCommand(new MenuCommand(Execute, new CommandID(CommandSet, CommandId)));
        }

        public static async Task InitializeAsync(AsyncPackage package)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);
            var commandService = await package.GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService;
            new HppExplainCommand(package, commandService);
        }

        void Execute(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (!(package.GetServiceAsync(typeof(DTE)).GetAwaiter().GetResult() is DTE dte) || dte.ActiveDocument == null)
            {
                HppOutputPane.WriteLine("[PolarisEvent] No active document.");
                return;
            }

            string filePath = dte.ActiveDocument.FullName;
            if (!filePath.EndsWith(".phxx", StringComparison.OrdinalIgnoreCase))
            {
                HppOutputPane.WriteLine("[PolarisEvent] Active document is not a .phxx file.");
                return;
            }

            string text = ReadActiveDocumentText(dte.ActiveDocument);
            string directory = Path.GetDirectoryName(filePath);
            var aliasFile = HppAliasFileLocator.FindAliasSource(directory);

            var project = new HppProject
            {
                Namespace = "editor.explain",
                Files = new[] { new SourceText(filePath, text) },
                AliasFile = aliasFile,
            };

            var result = new HppCompiler().Compile(project, CancellationToken.None);

            HppOutputPane.WriteLine($"[PolarisEvent] Explain: {filePath}");
            foreach (var d in result.Diagnostics)
            {
                HppOutputPane.WriteLine($"  {d.Span} {d.Code} [{d.Severity}] {d.Message}" + (d.Suggestion != null ? " " + d.Suggestion : string.Empty));
            }

            if (!result.Success || result.Files.Count == 0)
            {
                HppOutputPane.WriteLine("[PolarisEvent] Compilation failed; no CMD was generated.");
                return;
            }

            foreach (var file in result.Files)
            {
                HppOutputPane.WriteLine("---- Generated CMD ----");
                HppOutputPane.WriteLine(file.CommandText);
                HppOutputPane.WriteLine("------------------------");
            }
        }

        static string ReadActiveDocumentText(Document document)
        {
            if (document.Object("TextDocument") is TextDocument textDocument)
            {
                return textDocument.StartPoint.CreateEditPoint().GetText(textDocument.EndPoint);
            }

            return File.ReadAllText(document.FullName);
        }
    }
}
