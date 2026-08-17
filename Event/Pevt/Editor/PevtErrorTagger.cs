using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Adornments;
using Microsoft.VisualStudio.Text.Tagging;
using Microsoft.VisualStudio.Utilities;
using Polaris.Pevt.Diagnostics;

namespace PolarisTools.Event.Pevt.Editor
{
    [Export(typeof(ITaggerProvider))]
    [ContentType(PevtContentType.Name)]
    [TagType(typeof(IErrorTag))]
    internal sealed class PevtErrorTaggerProvider : ITaggerProvider
    {
        public ITagger<T>? CreateTagger<T>(ITextBuffer buffer) where T : ITag =>
            buffer.Properties.GetOrCreateSingletonProperty(() => new PevtErrorTagger(buffer)) as ITagger<T>;
    }

    /// <summary>
    /// 实时诊断波浪线。
    ///
    /// 三条计划明确要求的行为：
    ///
    ///   防抖      每次编辑重排一次延迟任务，连续敲字期间不会反复跑完整静态分析。
    ///   版本取消  上一轮还没跑完就被新编辑取代时，直接取消它，而不是等它白跑完。
    ///   过期丢弃  结果回来时如果缓冲区已经前进到别的版本，这一轮结果整份扔掉。
    ///
    /// 三者缺一都会有可观察的后果：没有防抖会在长事件上边打字边卡；没有取消会堆起一串后台分析；
    /// 没有过期丢弃会把波浪线画在早就改过的位置上。
    /// </summary>
    internal sealed class PevtErrorTagger : ITagger<IErrorTag>, IDisposable
    {
        /// <summary>停手多久之后才真正跑一次完整静态分析。</summary>
        private static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(300);

        private readonly ITextBuffer _buffer;
        private readonly object _gate = new object();

        private CancellationTokenSource? _pending;
        private PevtDiagnosticsResult? _result;
        private bool _disposed;

        public PevtErrorTagger(ITextBuffer buffer)
        {
            _buffer = buffer;
            _buffer.Changed += OnBufferChanged;
            Schedule();
        }

        public event EventHandler<SnapshotSpanEventArgs>? TagsChanged;

        public IEnumerable<ITagSpan<IErrorTag>> GetTags(NormalizedSnapshotSpanCollection spans)
        {
            PevtDiagnosticsResult? result = _result;
            if (result == null || spans.Count == 0)
                yield break;

            ITextSnapshot snapshot = spans[0].Snapshot;

            // 结果是为某个版本算的；缓冲区已经前进的话不画——下一轮马上就到。
            if (result.Version != snapshot.Version.VersionNumber)
                yield break;

            foreach (Diagnostic diagnostic in result.Diagnostics)
            {
                if (diagnostic.Location == null)
                    continue;

                SnapshotSpan? target = ToSnapshotSpan(snapshot, diagnostic);
                if (target == null)
                    continue;

                bool intersects = false;
                foreach (SnapshotSpan requested in spans)
                {
                    if (target.Value.IntersectsWith(requested))
                    {
                        intersects = true;
                        break;
                    }
                }

                if (!intersects)
                    continue;

                string errorType = diagnostic.Severity == DiagnosticSeverity.Error
                    ? PredefinedErrorTypeNames.SyntaxError
                    : PredefinedErrorTypeNames.Warning;

                yield return new TagSpan<IErrorTag>(
                    target.Value,
                    new ErrorTag(errorType, diagnostic.Id + ": " + diagnostic.Message));
            }
        }

        /// <summary>
        /// 把诊断跨度夹进快照范围。
        ///
        /// 零长度跨度（"缺了个 token"这类诊断的典型形态）扩成一个字符，否则波浪线画不出来；
        /// 落在文档末尾时向左借一个字符。
        /// </summary>
        private static SnapshotSpan? ToSnapshotSpan(ITextSnapshot snapshot, Diagnostic diagnostic)
        {
            Polaris.Pevt.Text.TextSpan span = diagnostic.Location!.Span;

            int start = Math.Max(0, Math.Min(span.Start, snapshot.Length));
            int end = Math.Max(start, Math.Min(span.End, snapshot.Length));

            if (end == start)
            {
                if (end < snapshot.Length)
                    end = start + 1;
                else if (start > 0)
                    start = end - 1;
                else
                    return null;
            }

            return new SnapshotSpan(snapshot, new Span(start, end - start));
        }

        private void OnBufferChanged(object sender, TextContentChangedEventArgs e) => Schedule();

        private void Schedule()
        {
            CancellationTokenSource source;
            lock (_gate)
            {
                if (_disposed)
                    return;

                _pending?.Cancel();
                _pending?.Dispose();
                _pending = source = new CancellationTokenSource();
            }

            ITextSnapshot snapshot = _buffer.CurrentSnapshot;
            int version = snapshot.Version.VersionNumber;
            string text = snapshot.GetText();

            _ = RunAsync(text, version, source.Token);
        }

        private async Task RunAsync(string text, int version, CancellationToken cancellationToken)
        {
            try
            {
                await Task.Delay(Debounce, cancellationToken).ConfigureAwait(false);

                PevtDiagnosticsResult result = await Task.Run(
                    () => PevtLiveDiagnostics.Analyze(text, version, cancellationToken),
                    cancellationToken).ConfigureAwait(false);

                if (cancellationToken.IsCancellationRequested)
                    return;

                // 结果落地之前再确认一次版本：等待期间用户完全可能又改了。
                if (_buffer.CurrentSnapshot.Version.VersionNumber != version)
                    return;

                _result = result;
                RaiseTagsChanged();
            }
            catch (OperationCanceledException)
            {
                // 被新的编辑取代，正常路径。
            }
            catch (Exception)
            {
                // 一次分析失败不该让编辑器失去响应，也不该反复弹窗；这一轮不出结果即可。
            }
        }

        private void RaiseTagsChanged()
        {
            EventHandler<SnapshotSpanEventArgs>? handler = TagsChanged;
            if (handler == null)
                return;

            ITextSnapshot snapshot = _buffer.CurrentSnapshot;
            handler(this, new SnapshotSpanEventArgs(new SnapshotSpan(snapshot, 0, snapshot.Length)));
        }

        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed)
                    return;

                _disposed = true;
                _pending?.Cancel();
                _pending?.Dispose();
                _pending = null;
            }

            _buffer.Changed -= OnBufferChanged;
        }
    }
}
