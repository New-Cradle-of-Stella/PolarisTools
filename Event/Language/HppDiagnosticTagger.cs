using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Adornments;
using Microsoft.VisualStudio.Text.Tagging;
using Polaris.Event.Compiler.Diagnostics;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace PolarisTools.Event.Language
{
    /// <summary>
    /// 实现计划 §6.3 的实时错误：buffer 改动、alias/同目录 .phxx 磁盘变化后 300ms debounce，异步跑
    /// <see cref="HppDiagnosticsService"/>，回到 UI 线程后把结果转成波浪线 + 推给 <see cref="HppErrorTableDataSource"/>
    /// 供 Error List 显示。两条触发路径（buffer 自己改、旁边文件改）最终都走同一个 <see cref="ScheduleAnalysis"/>，
    /// 保证波浪线和 Error List 永远来自同一次分析结果，不会各自为政。
    /// </summary>
    internal sealed class HppDiagnosticTagger : ITagger<IErrorTag>, IDisposable
    {
        static readonly TimeSpan DebounceInterval = TimeSpan.FromMilliseconds(300);

        readonly ITextBuffer buffer;
        readonly string filePath;
        readonly HppErrorTableDataSource errorTable;
        readonly Dispatcher dispatcher;
        readonly DispatcherTimer debounceTimer;
        readonly HashSet<string> watchedDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        CancellationTokenSource analysisCancellation = new CancellationTokenSource();
        IReadOnlyList<HppDiagnostic> diagnostics = Array.Empty<HppDiagnostic>();
        ITextSnapshot analyzedSnapshot;
        bool disposed;

        /// <summary>给 <see cref="HppSuggestedActionsSource"/> 复用同一份最近分析结果，不用重新跑一次编译器。</summary>
        internal IReadOnlyList<HppDiagnostic> CurrentDiagnostics => diagnostics;
        internal ITextSnapshot AnalyzedSnapshot => analyzedSnapshot;

        /// <summary>Error 波浪线 tagger 和 Code Action 数据源必须是同一个实例（同一份诊断结果），
        /// 不管谁先被 MEF 触发创建，都通过这一个入口拿。</summary>
        internal static HppDiagnosticTagger GetOrCreate(ITextBuffer buffer, string filePath, HppErrorTableDataSource errorTable)
            => buffer.Properties.GetOrCreateSingletonProperty(() => new HppDiagnosticTagger(buffer, filePath, errorTable));

        public HppDiagnosticTagger(ITextBuffer buffer, string filePath, HppErrorTableDataSource errorTable)
        {
            this.buffer = buffer;
            this.filePath = filePath;
            this.errorTable = errorTable;
            dispatcher = Dispatcher.CurrentDispatcher;

            debounceTimer = new DispatcherTimer { Interval = DebounceInterval };
            debounceTimer.Tick += (s, e) =>
            {
                debounceTimer.Stop();
                ScheduleAnalysis();
            };

            buffer.Changed += OnBufferChanged;
            HppWorkspaceService.DirectoryChanged += OnWatchedDirectoryChanged;
            ScheduleAnalysis();
        }

        public event EventHandler<SnapshotSpanEventArgs> TagsChanged;

        void OnBufferChanged(object sender, TextContentChangedEventArgs e)
        {
            Debounce();
        }

        void OnWatchedDirectoryChanged(object sender, string directory)
        {
            if (!watchedDirectories.Contains(directory))
            {
                return;
            }

            dispatcher.BeginInvoke(new Action(Debounce));
        }

        void Debounce()
        {
            debounceTimer.Stop();
            debounceTimer.Start();
        }

        void ScheduleAnalysis()
        {
            analysisCancellation.Cancel();
            analysisCancellation = new CancellationTokenSource();
            var token = analysisCancellation.Token;

            var snapshot = buffer.CurrentSnapshot;
            string text = snapshot.GetText();

            Task.Run(() => HppDiagnosticsService.AnalyzeFile(filePath, text, token), token)
                .ContinueWith(
                    t =>
                    {
                        if (t.IsCanceled || t.IsFaulted)
                        {
                            return;
                        }

                        dispatcher.BeginInvoke(new Action(() => ApplyResults(snapshot, t.Result)));
                    },
                    CancellationToken.None,
                    TaskContinuationOptions.OnlyOnRanToCompletion,
                    TaskScheduler.Default);
        }

        void ApplyResults(ITextSnapshot snapshot, HppEditorAnalysisResult result)
        {
            if (disposed)
            {
                return;
            }

            analyzedSnapshot = snapshot;
            diagnostics = result?.Diagnostics ?? Array.Empty<HppDiagnostic>();
            TagsChanged?.Invoke(this, new SnapshotSpanEventArgs(new SnapshotSpan(snapshot, 0, snapshot.Length)));

            errorTable?.UpdateFile(filePath, diagnostics);

            if (result != null)
            {
                Watch(result.FileDirectory);
                Watch(result.AliasDirectory);
            }
        }

        void Watch(string directory)
        {
            if (string.IsNullOrEmpty(directory) || !watchedDirectories.Add(directory))
            {
                return;
            }

            HppWorkspaceService.EnsureWatching(directory);
        }

        public IEnumerable<ITagSpan<IErrorTag>> GetTags(NormalizedSnapshotSpanCollection spans)
        {
            if (spans.Count == 0 || diagnostics.Count == 0 || analyzedSnapshot == null)
            {
                yield break;
            }

            var snapshot = spans[0].Snapshot;

            foreach (var d in diagnostics)
            {
                if (d.Severity == DiagnosticSeverity.Info)
                {
                    continue;
                }

                SnapshotSpan span;
                try
                {
                    span = ToSnapshotSpan(d, analyzedSnapshot).TranslateTo(snapshot, SpanTrackingMode.EdgeExclusive);
                }
                catch (ArgumentOutOfRangeException)
                {
                    continue;
                }

                if (!Overlaps(spans, span))
                {
                    continue;
                }

                string tagType = d.Severity == DiagnosticSeverity.Error
                    ? PredefinedErrorTypeNames.SyntaxError
                    : PredefinedErrorTypeNames.Warning;

                string message = d.Suggestion == null ? $"{d.Code}: {d.Message}" : $"{d.Code}: {d.Message} {d.Suggestion}";
                yield return new TagSpan<IErrorTag>(span, new ErrorTag(tagType, message));
            }
        }

        static bool Overlaps(NormalizedSnapshotSpanCollection spans, SnapshotSpan span)
        {
            foreach (var s in spans)
            {
                if (s.OverlapsWith(span) || s.Contains(span.Start))
                {
                    return true;
                }
            }

            return false;
        }

        internal static SnapshotSpan ToSnapshotSpan(HppDiagnostic d, ITextSnapshot snapshot)
        {
            int lineNumber = Math.Max(0, Math.Min(snapshot.LineCount - 1, d.Span.Line - 1));
            var line = snapshot.GetLineFromLineNumber(lineNumber);
            int column = Math.Max(0, d.Span.Column - 1);
            int start = Math.Min(line.Start.Position + column, line.End.Position);
            int length = Math.Max(1, line.End.Position - start);
            return new SnapshotSpan(snapshot, start, length);
        }

        public void Dispose()
        {
            disposed = true;
            buffer.Changed -= OnBufferChanged;
            HppWorkspaceService.DirectoryChanged -= OnWatchedDirectoryChanged;
            debounceTimer.Stop();
            analysisCancellation.Cancel();
            errorTable?.RemoveFile(filePath);
        }
    }
}
