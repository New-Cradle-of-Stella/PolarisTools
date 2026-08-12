using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Polaris.Event.Compiler.Diagnostics;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace PolarisTools.Event.Language
{
    /// <summary>
    /// 阶段5 §8 的 Code Action：拼写建议一键应用 + 一键创建缺失别名。复用
    /// <see cref="HppDiagnosticTagger"/> 已经算好的那份诊断，不重新跑一次编译器，
    /// 保证光标处显示的建议和波浪线/Error List 永远来自同一次分析结果。
    /// </summary>
    internal sealed class HppSuggestedActionsSource : ISuggestedActionsSource
    {
        static readonly Regex QuotedName = new Regex(@"'([^']+)'");
        static readonly Regex SuggestionName = new Regex(@":\s*([^\?]+)\??\s*$");

        readonly ITextBuffer buffer;
        readonly string filePath;
        readonly HppErrorTableDataSource errorTable;

        public HppSuggestedActionsSource(ITextBuffer buffer, string filePath, HppErrorTableDataSource errorTable)
        {
            this.buffer = buffer;
            this.filePath = filePath;
            this.errorTable = errorTable;
        }

        public event EventHandler<EventArgs> SuggestedActionsChanged { add { } remove { } }

        public Task<bool> HasSuggestedActionsAsync(ISuggestedActionCategorySet requestedActionCategories, SnapshotSpan range, CancellationToken cancellationToken)
        {
            return Task.FromResult(BuildActions(range).Any());
        }

        public IEnumerable<SuggestedActionSet> GetSuggestedActions(ISuggestedActionCategorySet requestedActionCategories, SnapshotSpan range, CancellationToken cancellationToken)
        {
            var actions = BuildActions(range).ToList();
            if (actions.Count == 0)
            {
                return Array.Empty<SuggestedActionSet>();
            }

            return new[] { new SuggestedActionSet(PredefinedSuggestedActionCategoryNames.CodeFix, actions) };
        }

        IEnumerable<HppSuggestedAction> BuildActions(SnapshotSpan range)
        {
            var tagger = HppDiagnosticTagger.GetOrCreate(buffer, filePath, errorTable);
            var snapshot = tagger.AnalyzedSnapshot;
            if (snapshot == null)
            {
                yield break;
            }

            foreach (var d in tagger.CurrentDiagnostics)
            {
                SnapshotSpan diagnosticSpan;
                try
                {
                    diagnosticSpan = HppDiagnosticTagger.ToSnapshotSpan(d, snapshot).TranslateTo(range.Snapshot, SpanTrackingMode.EdgeExclusive);
                }
                catch (ArgumentOutOfRangeException)
                {
                    continue;
                }

                if (!diagnosticSpan.IntersectsWith(range) && diagnosticSpan.Start != range.Start)
                {
                    continue;
                }

                foreach (var action in BuildActionsForDiagnostic(d))
                {
                    yield return action;
                }
            }
        }

        IEnumerable<HppSuggestedAction> BuildActionsForDiagnostic(HppDiagnostic d)
        {
            var badNameMatch = QuotedName.Match(d.Message);
            string badName = badNameMatch.Success ? badNameMatch.Groups[1].Value : null;

            if (badName != null && d.Suggestion != null)
            {
                var suggestMatch = SuggestionName.Match(d.Suggestion);
                if (suggestMatch.Success)
                {
                    string replacement = suggestMatch.Groups[1].Value.Trim();
                    yield return new HppSuggestedAction($"改为 '{replacement}'", () => ApplyReplacement(d, badName, replacement));
                }
            }

            if (badName == null)
            {
                yield break;
            }

            switch (d.Code)
            {
                case DiagnosticCodes.UnknownActor:
                    yield return new HppSuggestedAction($"在别名文件里创建角色 '{badName}'", () => CreateActor(badName));
                    break;
                case DiagnosticCodes.UnknownPosition:
                    yield return new HppSuggestedAction($"在别名文件里创建站位 '{badName}'", () => CreatePosition(badName));
                    break;
                case DiagnosticCodes.UnknownSfxAlias:
                    yield return new HppSuggestedAction($"在别名文件里创建音效别名 '{badName}'", () => CreateSfx(badName));
                    break;
                case DiagnosticCodes.UnknownEventAlias:
                    yield return new HppSuggestedAction($"在别名文件里创建事件别名 '{badName}'", () => CreateEvent(badName));
                    break;
            }
        }

        void ApplyReplacement(HppDiagnostic d, string badName, string replacement)
        {
            var snapshot = buffer.CurrentSnapshot;
            int lineNumber = Math.Max(0, Math.Min(snapshot.LineCount - 1, d.Span.Line - 1));
            var line = snapshot.GetLineFromLineNumber(lineNumber);
            string text = line.GetText();
            int index = text.IndexOf(badName, StringComparison.Ordinal);
            if (index < 0)
            {
                return;
            }

            buffer.Replace(new Span(line.Start.Position + index, badName.Length), replacement);
        }

        void CreateActor(string name) => EditAliasFile(text =>
            HppAliasFileEditor.AddSimpleEntry(text, "actors", name, new[] { "raw: TODO", "poses: {}" }));

        void CreatePosition(string name) => EditAliasFile(text =>
            HppAliasFileEditor.AddSimpleEntry(text, "positions", name, new[] { "talker: TODO", "boxPos: TODO", "from: TODO" }));

        void CreateSfx(string name) => EditAliasFile(text =>
            HppAliasFileEditor.AddLeafEntry(text, "audio", "sfx", name, "TODO"));

        void CreateEvent(string name) => EditAliasFile(text =>
            HppAliasFileEditor.AddFlatLeafEntry(text, "events", name, "TODO"));

        void EditAliasFile(Func<string, string> transform)
        {
            string directory = string.IsNullOrEmpty(filePath) ? null : Path.GetDirectoryName(filePath);
            var existing = HppAliasFileLocator.FindAliasSource(directory);

            string aliasPath = existing?.Path ?? Path.Combine(directory ?? ".", "polaris.events.yaml");
            string text = existing?.Content ?? HppAliasFileEditor.DefaultSkeleton;

            File.WriteAllText(aliasPath, transform(text));
        }

        public bool TryGetTelemetryId(out Guid telemetryId)
        {
            telemetryId = Guid.Empty;
            return false;
        }

        public void Dispose()
        {
        }
    }
}
