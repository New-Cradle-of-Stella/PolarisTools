using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Shell.TableControl;
using Microsoft.VisualStudio.Shell.TableManager;
using Microsoft.VisualStudio.Utilities;
using Polaris.Event.Compiler.Diagnostics;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;

namespace PolarisTools.Event.Language
{
    /// <summary>
    /// 阶段4 §6.1 提到但上一轮跳过的 Error List 表格数据源。走标准的
    /// "ITableDataSource + 每个订阅者一个 SinkManager + AddEntries/RemoveEntries" 模式（不用
    /// ITableEntriesSnapshotFactory 那一套版本化快照——我们诊断数量小，直接整份替换足够，也更容易
    /// 保证正确）。<see cref="HppDiagnosticTagger"/> 每次分析完，波浪线和这里的 Error List 行
    /// 用同一份诊断结果更新，不会漂移。
    /// </summary>
    [Export(typeof(ITableDataSource))]
    [Name(Name)]
    internal sealed class HppErrorTableDataSource : ITableDataSource
    {
        internal const string Name = "Polaris Event (哈++)";

        readonly object gate = new object();
        readonly List<SinkManager> managers = new List<SinkManager>();
        readonly Dictionary<string, List<HppTableEntry>> entriesByFile = new Dictionary<string, List<HppTableEntry>>(StringComparer.OrdinalIgnoreCase);

        [ImportingConstructor]
        public HppErrorTableDataSource(ITableManagerProvider tableManagerProvider)
        {
            var manager = tableManagerProvider.GetTableManager(StandardTables.ErrorsTable);
            manager.AddSource(
                this,
                StandardTableColumnDefinitions.DetailsExpander,
                StandardTableColumnDefinitions.ErrorSeverity,
                StandardTableColumnDefinitions.ErrorCode,
                StandardTableColumnDefinitions.Text,
                StandardTableColumnDefinitions.DocumentName,
                StandardTableColumnDefinitions.Line,
                StandardTableColumnDefinitions.Column,
                StandardTableColumnDefinitions.BuildTool);
        }

        public string SourceTypeIdentifier => StandardTableDataSources.ErrorTableDataSource;
        public string Identifier => Name;
        public string DisplayName => Name;

        public IDisposable Subscribe(ITableDataSink sink)
        {
            lock (gate)
            {
                var manager = new SinkManager(this, sink);
                managers.Add(manager);
                foreach (var entries in entriesByFile.Values)
                {
                    sink.AddEntries(entries.Cast<ITableEntry>().ToList());
                }

                return manager;
            }
        }

        public void UpdateFile(string filePath, IReadOnlyList<HppDiagnostic> diagnostics)
        {
            if (string.IsNullOrEmpty(filePath))
            {
                return;
            }

            var newEntries = (diagnostics ?? Array.Empty<HppDiagnostic>())
                .Select(d => new HppTableEntry(filePath, d))
                .ToList();

            List<HppTableEntry> oldEntries;
            List<SinkManager> snapshot;
            lock (gate)
            {
                entriesByFile.TryGetValue(filePath, out oldEntries);
                entriesByFile[filePath] = newEntries;
                snapshot = new List<SinkManager>(managers);
            }

            foreach (var manager in snapshot)
            {
                manager.UpdateSink(oldEntries, newEntries);
            }
        }

        public void RemoveFile(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
            {
                return;
            }

            List<HppTableEntry> oldEntries;
            List<SinkManager> snapshot;
            lock (gate)
            {
                entriesByFile.TryGetValue(filePath, out oldEntries);
                entriesByFile.Remove(filePath);
                snapshot = new List<SinkManager>(managers);
            }

            if (oldEntries == null || oldEntries.Count == 0)
            {
                return;
            }

            foreach (var manager in snapshot)
            {
                manager.UpdateSink(oldEntries, new List<HppTableEntry>());
            }
        }

        void RemoveManager(SinkManager manager)
        {
            lock (gate)
            {
                managers.Remove(manager);
            }
        }

        sealed class SinkManager : IDisposable
        {
            readonly HppErrorTableDataSource source;
            readonly ITableDataSink sink;

            public SinkManager(HppErrorTableDataSource source, ITableDataSink sink)
            {
                this.source = source;
                this.sink = sink;
            }

            public void UpdateSink(List<HppTableEntry> oldEntries, List<HppTableEntry> newEntries)
            {
                if (oldEntries != null && oldEntries.Count > 0)
                {
                    sink.RemoveEntries(oldEntries.Cast<ITableEntry>().ToList());
                }

                if (newEntries != null && newEntries.Count > 0)
                {
                    sink.AddEntries(newEntries.Cast<ITableEntry>().ToList());
                }
            }

            public void Dispose() => source.RemoveManager(this);
        }
    }

    internal sealed class HppTableEntry : ITableEntry
    {
        readonly string filePath;
        readonly HppDiagnostic diagnostic;

        public HppTableEntry(string filePath, HppDiagnostic diagnostic)
        {
            this.filePath = filePath;
            this.diagnostic = diagnostic;
        }

        public object Identity => (filePath, diagnostic.Code, diagnostic.Span.Line, diagnostic.Span.Column, diagnostic.Message);

        public bool TryGetValue(string keyName, out object content)
        {
            switch (keyName)
            {
                case StandardTableKeyNames.DocumentName:
                    content = filePath;
                    return true;
                case StandardTableKeyNames.Line:
                    content = Math.Max(0, diagnostic.Span.Line - 1);
                    return true;
                case StandardTableKeyNames.Column:
                    content = Math.Max(0, diagnostic.Span.Column - 1);
                    return true;
                case StandardTableKeyNames.Text:
                    content = diagnostic.Suggestion == null ? diagnostic.Message : $"{diagnostic.Message} {diagnostic.Suggestion}";
                    return true;
                case StandardTableKeyNames.ErrorCode:
                    content = diagnostic.Code;
                    return true;
                case StandardTableKeyNames.BuildTool:
                    content = "PolarisEvent";
                    return true;
                case StandardTableKeyNames.ErrorSeverity:
                    content = ToVsSeverity(diagnostic.Severity);
                    return true;
                default:
                    content = null;
                    return false;
            }
        }

        static __VSERRORCATEGORY ToVsSeverity(DiagnosticSeverity severity)
        {
            switch (severity)
            {
                case DiagnosticSeverity.Error:
                    return __VSERRORCATEGORY.EC_ERROR;
                case DiagnosticSeverity.Warning:
                    return __VSERRORCATEGORY.EC_WARNING;
                default:
                    return __VSERRORCATEGORY.EC_MESSAGE;
            }
        }

        public bool TrySetValue(string keyName, object content) => false;

        public bool CanSetValue(string keyName) => false;
    }
}
