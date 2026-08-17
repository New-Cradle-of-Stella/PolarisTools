using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Shell.TableControl;
using Microsoft.VisualStudio.Shell.TableManager;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Utilities;
using Polaris.Pevt.Diagnostics;

namespace PolarisTools.Event.Pevt.Editor
{
    /// <summary>
    /// 把 <c>.pevt</c> / <c>.pactor</c> 的实时诊断送进"错误列表"窗口。
    ///
    /// 波浪线（<c>PevtErrorTagger</c> / <c>PactorErrorTagger</c>）只在打开的编辑器里可见，而错误列表
    /// 是作者真正会去逐条点开的地方。两者读的是同一批诊断，因此不会出现"波浪线有、列表没有"。
    ///
    /// 数据源是**每个文档一个**：错误列表按源分组管理条目，文档关闭时整源移除，不必逐条清理。
    /// </summary>
    [Export(typeof(IWpfTextViewCreationListener))]
    [ContentType(PevtContentType.Name)]
    [ContentType(PolarisTools.Event.Actors.Editor.PactorContentType.Name)]
    [TextViewRole(PredefinedTextViewRoles.Document)]
    internal sealed class PevtErrorListRegistrar : IWpfTextViewCreationListener
    {
        [Import]
        internal ITableManagerProvider TableManagerProvider { get; set; } = null!;

        [Import]
        internal ITextDocumentFactoryService DocumentFactory { get; set; } = null!;

        public void TextViewCreated(IWpfTextView textView)
        {
            PevtProjectContext.DocumentFactory = DocumentFactory;

            if (!DocumentFactory.TryGetTextDocument(textView.TextBuffer, out ITextDocument document))
                return;

            PevtErrorListSource source = PevtErrorListSource.GetOrCreate(TableManagerProvider, textView.TextBuffer, document.FilePath);
            textView.Closed += (_, __) => source.Detach(textView);
            source.Attach(textView);
        }
    }

    /// <summary>一个文档的错误列表数据源。</summary>
    internal sealed class PevtErrorListSource : ITableDataSource
    {
        private const string SourceName = "PolarisTools.Pevt";

        private readonly ITextBuffer _buffer;
        private readonly string _filePath;
        private readonly List<ITableDataSink> _sinks = new List<ITableDataSink>();
        private readonly HashSet<ITextView> _views = new HashSet<ITextView>();
        private readonly object _gate = new object();

        private ITableManager? _manager;
        private IReadOnlyList<Diagnostic> _diagnostics = Array.Empty<Diagnostic>();

        private PevtErrorListSource(ITextBuffer buffer, string filePath)
        {
            _buffer = buffer;
            _filePath = filePath ?? string.Empty;
        }

        public static PevtErrorListSource GetOrCreate(ITableManagerProvider provider, ITextBuffer buffer, string filePath)
        {
            return buffer.Properties.GetOrCreateSingletonProperty(() =>
            {
                var source = new PevtErrorListSource(buffer, filePath);

                ITableManager manager = provider.GetTableManager(StandardTables.ErrorsTable);
                manager.AddSource(
                    source,
                    StandardTableColumnDefinitions.DocumentName,
                    StandardTableColumnDefinitions.Line,
                    StandardTableColumnDefinitions.Column,
                    StandardTableColumnDefinitions.ErrorCode,
                    StandardTableColumnDefinitions.ErrorSeverity,
                    StandardTableColumnDefinitions.Text);
                source._manager = manager;

                buffer.Changed += (_, __) => source.Refresh();
                source.Refresh();
                return source;
            });
        }

        // ---- ITableDataSource ----

        public string SourceTypeIdentifier => StandardTableDataSources.ErrorTableDataSource;

        public string Identifier => SourceName;

        public string DisplayName => "Polaris PEVT";

        public IDisposable Subscribe(ITableDataSink sink)
        {
            lock (_gate)
                _sinks.Add(sink);

            Publish(sink);
            return new Unsubscriber(this, sink);
        }

        internal void Attach(ITextView view)
        {
            lock (_gate)
                _views.Add(view);
        }

        /// <summary>最后一个视图关掉时整源退出错误列表——文档不再打开，它的条目不该继续留着。</summary>
        internal void Detach(ITextView view)
        {
            bool empty;
            lock (_gate)
            {
                _views.Remove(view);
                empty = _views.Count == 0;
            }

            if (!empty)
                return;

            _manager?.RemoveSource(this);
            _manager = null;
        }

        /// <summary>
        /// 重算诊断。
        ///
        /// 按扩展名决定走哪一条：<c>.pactor</c> 用目录 reader（PEVT91xx），其余按 <c>.pevt</c> 走
        /// 完整静态管线。两者都是同步调用，因为这里已经在缓冲区变更事件的后台节奏上，
        /// 而错误列表本身不要求逐帧刷新。
        /// </summary>
        private void Refresh()
        {
            try
            {
                ITextSnapshot snapshot = _buffer.CurrentSnapshot;
                string text = snapshot.GetText();

                bool isPactor = _filePath.EndsWith(
                    PolarisTools.Event.Actors.Editor.PactorContentType.FileExtension, StringComparison.OrdinalIgnoreCase);

                _diagnostics = isPactor
                    ? PolarisTools.Event.Actors.Editor.PactorLiveDiagnostics.Analyze(text, _filePath, default)
                    : PevtLiveDiagnostics.Analyze(text, snapshot.Version.VersionNumber, default).Diagnostics;
            }
            catch (Exception)
            {
                _diagnostics = Array.Empty<Diagnostic>();
            }

            List<ITableDataSink> sinks;
            lock (_gate)
                sinks = new List<ITableDataSink>(_sinks);

            foreach (ITableDataSink sink in sinks)
                Publish(sink);
        }

        private void Publish(ITableDataSink sink)
        {
            var entries = new List<ITableEntry>();
            foreach (Diagnostic diagnostic in _diagnostics)
                entries.Add(new PevtTableEntry(diagnostic, _filePath));

            sink.RemoveAllSnapshots();
            sink.AddEntries(entries, removeAllEntries: true);
        }

        private sealed class Unsubscriber : IDisposable
        {
            private readonly PevtErrorListSource _source;
            private readonly ITableDataSink _sink;

            public Unsubscriber(PevtErrorListSource source, ITableDataSink sink)
            {
                _source = source;
                _sink = sink;
            }

            public void Dispose()
            {
                lock (_source._gate)
                    _source._sinks.Remove(_sink);
            }
        }
    }

    /// <summary>错误列表里的一行。列名用 <see cref="StandardTableKeyNames"/>，双击定位靠行列号。</summary>
    internal sealed class PevtTableEntry : ITableEntry
    {
        private readonly Diagnostic _diagnostic;
        private readonly string _filePath;

        public PevtTableEntry(Diagnostic diagnostic, string filePath)
        {
            _diagnostic = diagnostic;
            _filePath = filePath;
        }

        public object Identity => _diagnostic;

        public bool CanSetValue(string keyName) => false;

        public bool TrySetValue(string keyName, object content) => false;

        public bool TryGetValue(string keyName, out object? content)
        {
            switch (keyName)
            {
                case StandardTableKeyNames.DocumentName:
                    content = _filePath;
                    return true;

                // 错误列表的行列是 0 基，诊断位置是 1 基。
                case StandardTableKeyNames.Line:
                    content = Math.Max(0, (_diagnostic.Location?.StartLine ?? 1) - 1);
                    return true;

                case StandardTableKeyNames.Column:
                    content = Math.Max(0, (_diagnostic.Location?.StartColumn ?? 1) - 1);
                    return true;

                case StandardTableKeyNames.ErrorCode:
                    content = _diagnostic.Id;
                    return true;

                case StandardTableKeyNames.Text:
                    content = _diagnostic.Message;
                    return true;

                case StandardTableKeyNames.ErrorSeverity:
                    content = _diagnostic.Severity == DiagnosticSeverity.Error
                        ? __VSERRORCATEGORY.EC_ERROR
                        : __VSERRORCATEGORY.EC_WARNING;
                    return true;

                case StandardTableKeyNames.BuildTool:
                    content = "Polaris PEVT";
                    return true;

                case StandardTableKeyNames.HelpLink:
                    content = null;
                    return false;

                default:
                    content = null;
                    return false;
            }
        }
    }
}
