using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Adornments;
using Microsoft.VisualStudio.Text.Tagging;
using Microsoft.VisualStudio.Utilities;
using Polaris.Pevt.Actors;
using Polaris.Pevt.Diagnostics;

namespace PolarisTools.Event.Actors.Editor
{
    /// <summary>
    /// <c>.pactor</c> 的内容类型。
    ///
    /// 以 <c>XML</c> 为基类型，所以 Visual Studio 自带的 XML 着色、折叠、标签配对与格式化直接生效。
    /// <c>.pactor</c> 是 XML 数据，不执行表达式、流程或任意 C#，因此"复用 XML 编辑器 + 加一层
    /// PEVT91xx 实时校验"比自造一个文本编辑器更贴合它的性质。
    /// </summary>
    internal static class PactorContentType
    {
        public const string Name = "pactor";

        public const string FileExtension = ".pactor";

#pragma warning disable 649
        [Export]
        [Name(Name)]
        [BaseDefinition("XML")]
        internal static ContentTypeDefinition? PactorContentTypeDefinition;

        [Export]
        [FileExtension(FileExtension)]
        [ContentType(Name)]
        internal static FileExtensionToContentTypeDefinition? PactorFileExtensionDefinition;
#pragma warning restore 649
    }

    /// <summary>
    /// <c>.pactor</c> 的实时校验。
    ///
    /// 走的是共享 Core 的 <see cref="ActorCatalogReader"/>——也就是游戏侧登记人物目录时用的同一个
    /// 严格 reader，因此编辑器里看到的 PEVT91xx 和游戏加载时报的完全一致：命名空间、Version、
    /// 局部 ID、颜色、重复项、引用完整性、DTD／外部实体拒绝、未知执行性元素全部在这一次读取里判定。
    ///
    /// 一律按外部目录（<see cref="ActorCatalogSourceKind.External"/>）读：内置 <c>aic</c> 目录是
    /// Polaris 自己的嵌入资源，作者手上编辑的 <c>.pactor</c> 只可能是外部目录。这样
    /// "外部来源不能伪造 BuiltIn 或 aic namespace"（PEVT9115 等）在编辑期就会报出来，
    /// 而不是等到装进游戏才失败。
    /// </summary>
    internal static class PactorLiveDiagnostics
    {
        public static IReadOnlyList<Diagnostic> Analyze(string xml, string path, CancellationToken cancellationToken)
        {
            try
            {
                ActorCatalogReadResult result = ActorCatalogReader.ReadText(
                    xml ?? string.Empty,
                    string.IsNullOrEmpty(path) ? "editor.pactor" : path,
                    ActorCatalogSourceKind.External,
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
    [ContentType(PactorContentType.Name)]
    [TagType(typeof(IErrorTag))]
    internal sealed class PactorErrorTaggerProvider : ITaggerProvider
    {
        public ITagger<T>? CreateTagger<T>(ITextBuffer buffer) where T : ITag =>
            buffer.Properties.GetOrCreateSingletonProperty(() => new PactorErrorTagger(buffer)) as ITagger<T>;
    }

    /// <summary>
    /// <c>.pactor</c> 的 PEVT91xx 波浪线。防抖、版本取消与过期丢弃三条纪律和 <c>.pevt</c> 那一侧完全一致
    /// （见 <c>PevtErrorTagger</c>）——目录 reader 会做 XML 解析加完整引用校验，在大目录上不便宜。
    /// </summary>
    internal sealed class PactorErrorTagger : ITagger<IErrorTag>, IDisposable
    {
        private static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(350);

        private readonly ITextBuffer _buffer;
        private readonly object _gate = new object();

        private CancellationTokenSource? _pending;
        private int _resultVersion = -1;
        private IReadOnlyList<Diagnostic> _result = Array.Empty<Diagnostic>();
        private bool _disposed;

        public PactorErrorTagger(ITextBuffer buffer)
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
                    () => PactorLiveDiagnostics.Analyze(text, path ?? string.Empty, cancellationToken),
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

    /// <summary>
    /// <c>.pactor</c> 的稳定写回。
    ///
    /// 计划要求"写回稳定可读的 UTF-8 XML，保留未知非执行性扩展节点"。两条都靠同一个做法实现：
    /// **改哪儿写哪儿**——用 <see cref="XDocument"/> 载入原文档、只改动目标属性或元素、再原样写出。
    /// 不重新生成整棵树，因此：
    ///
    ///   未知扩展节点原封不动（重新生成会把它们丢掉，那正是"保留未知非执行性扩展节点"要防的事）；
    ///   属性顺序、注释、空行不变，所以两次保存之间的 diff 只包含真正改过的行；
    ///   UTF-8 无 BOM，缩进两空格，与生成器和内置目录的排版一致。
    ///
    /// 这一层不做视觉编辑 UI；它是给未来那个 <c>.pactor</c> 设计器用的写回内核，也可以被脚本直接调用。
    /// </summary>
    internal static class PactorWriter
    {
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        /// <summary>把 XML 文本写成稳定形式。语法不合法时原样返回，绝不"顺手修好"作者正在编辑的文档。</summary>
        public static string Normalize(string xml)
        {
            try
            {
                XDocument document = XDocument.Parse(xml, LoadOptions.PreserveWhitespace | LoadOptions.SetLineInfo);
                return Render(document);
            }
            catch (Exception)
            {
                return xml;
            }
        }

        /// <summary>
        /// 改一个人物的属性并返回新 XML；找不到该人物时原样返回。
        /// <paramref name="value"/> 为 null 表示删除该属性。
        /// </summary>
        public static string SetActorAttribute(string xml, string localId, string attributeName, string? value)
        {
            try
            {
                XDocument document = XDocument.Parse(xml, LoadOptions.PreserveWhitespace | LoadOptions.SetLineInfo);
                XElement? actor = FindActor(document, localId);
                if (actor == null)
                    return xml;

                if (value == null)
                    actor.Attribute(attributeName)?.Remove();
                else
                    actor.SetAttributeValue(attributeName, value);

                return Render(document);
            }
            catch (Exception)
            {
                return xml;
            }
        }

        /// <summary>按 <c>Id</c> 找一个 <c>Actor</c> 元素。命名空间由文档自己决定，不写死。</summary>
        public static XElement? FindActor(XDocument document, string localId)
        {
            XElement? root = document.Root;
            if (root == null)
                return null;

            return root.Elements()
                .Where(e => e.Name.LocalName == "Actor")
                .FirstOrDefault(e => string.Equals((string?)e.Attribute("Id"), localId, StringComparison.Ordinal));
        }

        /// <summary>文档里全部人物的局部 ID，按文档顺序。</summary>
        public static IReadOnlyList<string> ActorLocalIds(string xml)
        {
            try
            {
                XDocument document = XDocument.Parse(xml);
                XElement? root = document.Root;
                if (root == null)
                    return Array.Empty<string>();

                return root.Elements()
                    .Where(e => e.Name.LocalName == "Actor")
                    .Select(e => (string?)e.Attribute("Id"))
                    .Where(id => !string.IsNullOrEmpty(id))
                    .Select(id => id!)
                    .ToList();
            }
            catch (Exception)
            {
                return Array.Empty<string>();
            }
        }

        private static string Render(XDocument document)
        {
            var settings = new System.Xml.XmlWriterSettings
            {
                Encoding = Utf8NoBom,
                Indent = true,
                IndentChars = "  ",
                NewLineChars = "\n",
                OmitXmlDeclaration = false,
            };

            var builder = new StringBuilder();
            using (System.Xml.XmlWriter writer = System.Xml.XmlWriter.Create(builder, settings))
                document.Save(writer);

            // XmlWriter 写的是 `encoding="utf-8"`；文件本身按 UTF-8 无 BOM 保存，两者一致。
            return builder.ToString();
        }
    }
}
