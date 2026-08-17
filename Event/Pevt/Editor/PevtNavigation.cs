using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Threading;
using EnvDTE;
using Microsoft.VisualStudio.Commanding;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Editor.Commanding.Commands;
using Microsoft.VisualStudio.Text.Tagging;
using Microsoft.VisualStudio.Utilities;
using Polaris.Pevt.Actors;

namespace PolarisTools.Event.Pevt.Editor
{
    /// <summary>
    /// 转到定义。
    ///
    /// 三类目标：
    ///   变量／常量／句柄／事件块／标签 → 本文件内的声明处，直接移动插入点。
    ///   人物 ID／appearance／anchor    → 定义它的 <c>.pactor</c>，打开并定位到那一行。
    ///   <c>callevt</c> 目标与跨模组人物 → 不跳。计划明确要求"不假定目标在当前项目"：
    ///                                     事件 ID 是运行时全局查询，跨模组人物可以晚于本项目注册，
    ///                                     跳到一个猜出来的位置比不跳更糟。
    /// </summary>
    [Export(typeof(ICommandHandler))]
    [ContentType(PevtContentType.Name)]
    [Name("PevtGoToDefinition")]
    internal sealed class PevtGoToDefinitionHandler : ICommandHandler<GoToDefinitionCommandArgs>
    {
        public string DisplayName => "转到 PEVT 定义";

        public CommandState GetCommandState(GoToDefinitionCommandArgs args) => CommandState.Available;

        public bool ExecuteCommand(GoToDefinitionCommandArgs args, CommandExecutionContext executionContext)
        {
            try
            {
                ITextSnapshot snapshot = args.SubjectBuffer.CurrentSnapshot;
                int position = args.TextView.Caret.Position.BufferPosition.Position;

                PevtSemanticModel? model = PevtSemanticModel.Create(snapshot.GetText());
                PevtSymbolOccurrence? occurrence = model?.FindAt(position);
                if (model == null || occurrence == null)
                    return false;

                // 先试本文件内的声明。
                PevtSymbolOccurrence? declaration = model.FindDeclaration(occurrence);
                if (declaration != null && declaration.Span.Start != occurrence.Span.Start)
                {
                    var point = new SnapshotPoint(snapshot, Math.Min(declaration.Span.Start, snapshot.Length));
                    args.TextView.Caret.MoveTo(point);
                    args.TextView.ViewScroller.EnsureSpanVisible(new SnapshotSpan(point, 0));
                    return true;
                }

                // 字符串字面量里的人物 ID → .pactor。
                return TryNavigateToActor(args, snapshot, position);
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// 光标落在字符串字面量上时，把它当人物 ID 在合并目录里查一次；命中就打开对应 <c>.pactor</c>。
        /// </summary>
        private bool TryNavigateToActor(GoToDefinitionCommandArgs args, ITextSnapshot snapshot, int position)
        {
            string? literal = ReadStringLiteralAt(snapshot, position);
            if (string.IsNullOrEmpty(literal))
                return false;

            string? projectDirectory = PevtProjectContext.DirectoryFor(args.SubjectBuffer);
            if (!PevtActorIndex.TryFind(projectDirectory, literal!, out ActorCatalog catalog, out ActorDefinition actor))
                return false;

            // 内置目录是嵌入资源，没有可打开的磁盘文件；这属于"目标不在当前项目"的正常情况。
            if (catalog.IsBuiltIn || string.IsNullOrEmpty(catalog.SourcePath) || !System.IO.File.Exists(catalog.SourcePath))
                return false;

            return OpenAtActor(catalog.SourcePath, actor.LocalId);
        }

        /// <summary>读光标所在的双引号字符串内容；不在字符串里时为 null。</summary>
        private static string? ReadStringLiteralAt(ITextSnapshot snapshot, int position)
        {
            ITextSnapshotLine line = snapshot.GetLineFromPosition(Math.Min(position, snapshot.Length));
            string text = line.GetText();
            int offset = position - line.Start.Position;

            int start = -1;
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] != '"')
                    continue;

                if (start < 0)
                {
                    start = i;
                    continue;
                }

                if (offset > start && offset <= i)
                    return text.Substring(start + 1, i - start - 1);

                start = -1;
            }

            return null;
        }

        /// <summary>
        /// 打开 <c>.pactor</c> 并定位到该人物的 <c>Id="..."</c> 那一行。
        ///
        /// 用 DTE 而不是 <c>IVsUIShellOpenDocument</c>：仓库里其它工具（PlangCodeGenTrigger 等）
        /// 用的就是这条路径，保持一致比少一层抽象更重要。
        /// </summary>
        private static bool OpenAtActor(string pactorPath, string localId)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (Package.GetGlobalService(typeof(DTE)) is not DTE dte)
                return false;

            Window window = dte.ItemOperations.OpenFile(pactorPath, Constants.vsViewKindTextView);
            if (window?.Document?.Selection is not TextSelection selection)
                return true; // 文件已经打开，定位失败不算命令失败。

            string needle = "Id=\"" + localId + "\"";
            string[] lines = System.IO.File.ReadAllLines(pactorPath);
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].Contains(needle))
                {
                    selection.MoveToLineAndOffset(i + 1, 1);
                    return true;
                }
            }

            return true;
        }
    }

    /// <summary>
    /// 引用高亮：插入点所在符号在本文件里的全部出现。
    ///
    /// 计划要求"所有结果绑定文档版本并支持取消"，所以这里和诊断走同一套纪律——结果带版本号，
    /// 缓冲区前进就丢弃。这是"引用查找"在编辑器里最直接的形态；Find All References 面板
    /// （<c>IFindAllReferencesService</c>）还没接，见 README 的待办。
    /// </summary>
    [Export(typeof(IViewTaggerProvider))]
    [ContentType(PevtContentType.Name)]
    [TagType(typeof(ITextMarkerTag))]
    internal sealed class PevtReferenceHighlightProvider : IViewTaggerProvider
    {
        public ITagger<T>? CreateTagger<T>(ITextView textView, ITextBuffer buffer) where T : ITag
        {
            if (textView.TextBuffer != buffer)
                return null;

            return textView.Properties.GetOrCreateSingletonProperty(
                () => new PevtReferenceHighlightTagger(textView, buffer)) as ITagger<T>;
        }
    }

    internal sealed class PevtReferenceHighlightTagger : ITagger<ITextMarkerTag>, IDisposable
    {
        private static readonly TextMarkerTag Marker = new TextMarkerTag("MarkerFormatDefinition/HighlightedReference");

        private readonly ITextView _view;
        private readonly ITextBuffer _buffer;

        private int _version = -1;
        private int _caret = -1;
        private List<Span> _spans = new List<Span>();
        private bool _disposed;

        public PevtReferenceHighlightTagger(ITextView view, ITextBuffer buffer)
        {
            _view = view;
            _buffer = buffer;
            _view.Caret.PositionChanged += OnCaretChanged;
            _view.TextBuffer.Changed += OnBufferChanged;
        }

        public event EventHandler<SnapshotSpanEventArgs>? TagsChanged;

        public IEnumerable<ITagSpan<ITextMarkerTag>> GetTags(NormalizedSnapshotSpanCollection spans)
        {
            if (spans.Count == 0)
                yield break;

            ITextSnapshot snapshot = spans[0].Snapshot;
            Refresh(snapshot);

            if (_version != snapshot.Version.VersionNumber)
                yield break;

            foreach (Span span in _spans)
            {
                if (span.End > snapshot.Length)
                    continue;

                var candidate = new SnapshotSpan(snapshot, span);
                foreach (SnapshotSpan requested in spans)
                {
                    if (candidate.IntersectsWith(requested))
                    {
                        yield return new TagSpan<ITextMarkerTag>(candidate, Marker);
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// 重算高亮。只在"版本或插入点变了"时做，因为编辑器会为每个可见区间分别调用
        /// <see cref="GetTags"/>，每次重新分析全文会让长事件的滚动明显发涩。
        /// </summary>
        private void Refresh(ITextSnapshot snapshot)
        {
            int caret = _view.Caret.Position.BufferPosition.Position;
            if (_version == snapshot.Version.VersionNumber && _caret == caret)
                return;

            _version = snapshot.Version.VersionNumber;
            _caret = caret;
            _spans = new List<Span>();

            try
            {
                PevtSemanticModel? model = PevtSemanticModel.Create(snapshot.GetText());
                PevtSymbolOccurrence? target = model?.FindAt(caret);
                if (model == null || target == null)
                    return;

                // `@` 名称与标签指向的是全局／文件级概念，高亮它们的全部出现同样有意义。
                foreach (PevtSymbolOccurrence occurrence in model.FindAll(target))
                {
                    int start = Math.Min(occurrence.Span.Start, snapshot.Length);
                    int length = Math.Min(occurrence.Span.Length, snapshot.Length - start);
                    if (length > 0)
                        _spans.Add(new Span(start, length));
                }
            }
            catch (Exception)
            {
                _spans = new List<Span>();
            }
        }

        private void OnCaretChanged(object sender, CaretPositionChangedEventArgs e) => Raise();

        private void OnBufferChanged(object sender, TextContentChangedEventArgs e) => Raise();

        private void Raise()
        {
            if (_disposed)
                return;

            ITextSnapshot snapshot = _buffer.CurrentSnapshot;
            TagsChanged?.Invoke(this, new SnapshotSpanEventArgs(new SnapshotSpan(snapshot, 0, snapshot.Length)));
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _view.Caret.PositionChanged -= OnCaretChanged;
            _view.TextBuffer.Changed -= OnBufferChanged;
        }
    }
}
