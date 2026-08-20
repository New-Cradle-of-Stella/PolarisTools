using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Adornments;
using Microsoft.VisualStudio.Text.Tagging;
using Microsoft.VisualStudio.Utilities;
using Polaris.Pevt.Actors;
using Polaris.Pevt.Diagnostics;

namespace PolarisTools.Event.Actors.Editor
{
    /// <summary>
    /// <c>.pactorx</c>（PEVT-E06 人物目录增量扩展 sidecar）的内容类型。
    ///
    /// 以 <c>XML</c> 为基类型，理由和 <see cref="PactorContentType"/> 完全一样：sidecar 是 XML 数据，
    /// 不执行表达式、流程或任意 C#。
    /// </summary>
    internal static class PactorExtensionContentType
    {
        public const string Name = "pactorx";

        public const string FileExtension = ".pactorx";

#pragma warning disable 649
        [Export]
        [Name(Name)]
        [BaseDefinition("XML")]
        internal static ContentTypeDefinition? PactorExtensionContentTypeDefinition;

        [Export]
        [FileExtension(FileExtension)]
        [ContentType(Name)]
        internal static FileExtensionToContentTypeDefinition? PactorExtensionFileExtensionDefinition;
#pragma warning restore 649
    }

    /// <summary>
    /// <c>.pactorx</c> 的实时校验。走的是共享 Core 的 <see cref="ActorCatalogReader.ReadExtension"/>——
    /// 也就是游戏侧应用扩展时用的同一个严格 reader，因此编辑器里看到的 PEVT91xx 和游戏加载时报的
    /// 完全一致。跨程序集的"目标是否存在"判定不在这里做：那要等全部目录登记完，编辑器只能看到
    /// 这一份 sidecar 自身的形状错误（未知元素、禁止覆盖、重复 appearance ID 等）。
    /// </summary>
    internal static class PactorExtensionLiveDiagnostics
    {
        public static IReadOnlyList<Diagnostic> Analyze(string xml, string path, CancellationToken cancellationToken)
        {
            try
            {
                ActorCatalogExtensionReadResult result = ActorCatalogReader.ReadExtensionText(
                    xml ?? string.Empty,
                    string.IsNullOrEmpty(path) ? "editor.pactorx" : path,
                    cancellationToken);

                return result.Diagnostics;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                return Array.Empty<Diagnostic>();
            }
        }
    }

    [Export(typeof(ITaggerProvider))]
    [ContentType(PactorExtensionContentType.Name)]
    [TagType(typeof(IErrorTag))]
    internal sealed class PactorExtensionErrorTaggerProvider : ITaggerProvider
    {
        public ITagger<T>? CreateTagger<T>(ITextBuffer buffer) where T : ITag =>
            buffer.Properties.GetOrCreateSingletonProperty(() => new PactorExtensionErrorTagger(buffer)) as ITagger<T>;
    }

    /// <summary>
    /// <c>.pactorx</c> 的 PEVT91xx 波浪线。防抖、版本取消与过期丢弃三条纪律和 <c>.pactor</c>
    /// 那一侧完全一致（见 <see cref="PactorErrorTagger"/>）。
    /// </summary>
    internal sealed class PactorExtensionErrorTagger : ITagger<IErrorTag>, IDisposable
    {
        private static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(350);

        private readonly ITextBuffer _buffer;
        private readonly object _gate = new object();

        private CancellationTokenSource? _pending;
        private int _resultVersion = -1;
        private IReadOnlyList<Diagnostic> _result = Array.Empty<Diagnostic>();
        private bool _disposed;

        public PactorExtensionErrorTagger(ITextBuffer buffer)
        {
            _buffer = buffer;
            _buffer.Changed += OnBufferChanged;
            Schedule();
        }

        public event EventHandler<SnapshotSpanEventArgs>? TagsChanged;

        public IEnumerable<ITagSpan<IErrorTag>> GetTags(NormalizedSnapshotSpanCollection spans)
        {
            if (spans.Count == 0)
                yield break;

            ITextSnapshot snapshot = spans[0].Snapshot;
            if (_resultVersion != snapshot.Version.VersionNumber)
                yield break;

            foreach (Diagnostic diagnostic in _result)
            {
                if (diagnostic.Location == null)
                    continue;

                Polaris.Pevt.Text.TextSpan span = diagnostic.Location.Span;
                int start = Math.Max(0, Math.Min(span.Start, snapshot.Length));
                int end = Math.Max(start, Math.Min(span.End, snapshot.Length));
                if (end == start)
                {
                    if (end < snapshot.Length)
                        end = start + 1;
                    else if (start > 0)
                        start = end - 1;
                    else
                        continue;
                }

                var candidate = new SnapshotSpan(snapshot, new Span(start, end - start));

                bool intersects = false;
                foreach (SnapshotSpan requested in spans)
                {
                    if (candidate.IntersectsWith(requested))
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

                yield return new TagSpan<IErrorTag>(candidate, new ErrorTag(errorType, diagnostic.Id + ": " + diagnostic.Message));
            }
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
            string? path = PolarisTools.Event.Pevt.Editor.PevtProjectContext.PathFor(_buffer);

            _ = RunAsync(text, path, version, source.Token);
        }

        private async Task RunAsync(string text, string? path, int version, CancellationToken cancellationToken)
        {
            try
            {
                await Task.Delay(Debounce, cancellationToken).ConfigureAwait(false);

                IReadOnlyList<Diagnostic> diagnostics = await Task.Run(
                    () => PactorExtensionLiveDiagnostics.Analyze(text, path ?? string.Empty, cancellationToken),
                    cancellationToken).ConfigureAwait(false);

                if (cancellationToken.IsCancellationRequested)
                    return;
                if (_buffer.CurrentSnapshot.Version.VersionNumber != version)
                    return;

                _result = diagnostics;
                _resultVersion = version;

                ITextSnapshot snapshot = _buffer.CurrentSnapshot;
                TagsChanged?.Invoke(this, new SnapshotSpanEventArgs(new SnapshotSpan(snapshot, 0, snapshot.Length)));
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception)
            {
            }
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
