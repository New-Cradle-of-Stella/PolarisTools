using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using Microsoft.VisualStudio.Language.StandardClassification;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Classification;
using Microsoft.VisualStudio.Utilities;
using Polaris.Pevt.Diagnostics;
using Polaris.Pevt.Syntax;
using Polaris.Pevt.Text;

namespace PolarisTools.Event.Pevt.Editor
{
    [Export(typeof(IClassifierProvider))]
    [ContentType(PevtContentType.Name)]
    internal sealed class PevtClassifierProvider : IClassifierProvider
    {
        [Import]
        internal IClassificationTypeRegistryService Registry { get; set; } = null!;

        public IClassifier GetClassifier(ITextBuffer buffer) =>
            buffer.Properties.GetOrCreateSingletonProperty(() => new PevtTokenClassifier(Registry));
    }

    /// <summary>
    /// 语法高亮。
    ///
    /// 用的是共享 Core 的词法器（<see cref="Lexer.Tokenize"/>），不是编辑器侧另写一套正则——
    /// 关键字表、字面量形态、原始文本块边界和注释规则因此永远和游戏侧一致。
    ///
    /// 计划允许"高亮可以使用轻量 token 快照"：这里只跑词法，不跑绑定与控制流。实时诊断是另一条
    /// 路径（<see cref="PevtErrorTagger"/>），那一条必须跑完整 Core。
    ///
    /// 整份文档只 tokenize 一次并按快照版本缓存：编辑器会为每个可见行分别调用
    /// <see cref="GetClassificationSpans"/>，每次重新扫全文会让长事件在滚动时明显卡顿。
    /// </summary>
    internal sealed class PevtTokenClassifier : IClassifier
    {
        private readonly IClassificationType _keyword;
        private readonly IClassificationType _comment;
        private readonly IClassificationType _string;
        private readonly IClassificationType _number;
        private readonly IClassificationType _operator;
        private readonly IClassificationType _builtinCall;
        private readonly IClassificationType _blockName;
        private readonly IClassificationType _label;
        private readonly IClassificationType _rawContent;

        private int _cachedVersion = -1;
        private List<ClassificationSpan>? _cached;
        private ITextSnapshot? _cachedSnapshot;

        public PevtTokenClassifier(IClassificationTypeRegistryService registry)
        {
            _keyword = registry.GetClassificationType(PredefinedClassificationTypeNames.Keyword);
            _comment = registry.GetClassificationType(PredefinedClassificationTypeNames.Comment);
            _string = registry.GetClassificationType(PredefinedClassificationTypeNames.String);
            _number = registry.GetClassificationType(PredefinedClassificationTypeNames.Number);
            _operator = registry.GetClassificationType(PredefinedClassificationTypeNames.Operator);
            _builtinCall = registry.GetClassificationType(PevtClassificationTypes.BuiltinCall);
            _blockName = registry.GetClassificationType(PevtClassificationTypes.BlockName);
            _label = registry.GetClassificationType(PevtClassificationTypes.Label);
            _rawContent = registry.GetClassificationType(PevtClassificationTypes.RawContent);
        }

        /// <summary>词法结果只随缓冲区内容变化；分类结果本身不会独立失效，所以这个事件不会触发。</summary>
        public event EventHandler<ClassificationChangedEventArgs>? ClassificationChanged;

        public IList<ClassificationSpan> GetClassificationSpans(SnapshotSpan span)
        {
            List<ClassificationSpan> all = GetOrBuild(span.Snapshot);

            var result = new List<ClassificationSpan>();
            foreach (ClassificationSpan candidate in all)
            {
                if (candidate.Span.IntersectsWith(span))
                    result.Add(candidate);
            }

            return result;
        }

        private List<ClassificationSpan> GetOrBuild(ITextSnapshot snapshot)
        {
            if (_cached != null && _cachedVersion == snapshot.Version.VersionNumber && ReferenceEquals(_cachedSnapshot, snapshot))
                return _cached;

            List<ClassificationSpan> built = Build(snapshot);
            _cached = built;
            _cachedVersion = snapshot.Version.VersionNumber;
            _cachedSnapshot = snapshot;
            return built;
        }

        private List<ClassificationSpan> Build(ITextSnapshot snapshot)
        {
            var result = new List<ClassificationSpan>();

            IReadOnlyList<SyntaxToken> tokens;
            try
            {
                SourceText? source = PevtEditorText.Load(snapshot.GetText());
                if (source == null)
                    return result;

                // 词法诊断在这里丢掉：高亮不负责报错，报错是 PevtErrorTagger 的事。
                tokens = Lexer.Tokenize(source, new DiagnosticBag());
            }
            catch (Exception)
            {
                // 词法器对任何输入都应该有输出，但高亮绝不能因为一次意外把编辑器拖下去。
                return result;
            }

            for (int i = 0; i < tokens.Count; i++)
            {
                SyntaxToken token = tokens[i];

                AddTrivia(result, snapshot, token.LeadingTrivia);

                IClassificationType? type = Classify(token, i > 0 ? tokens[i - 1] : null, i + 1 < tokens.Count ? tokens[i + 1] : null);
                if (type != null && !token.IsMissing && token.Span.Length > 0)
                    Add(result, snapshot, token.Span, type);

                AddTrivia(result, snapshot, token.TrailingTrivia);
            }

            return result;
        }

        private void AddTrivia(List<ClassificationSpan> result, ITextSnapshot snapshot, IReadOnlyList<SyntaxTrivia> trivia)
        {
            foreach (SyntaxTrivia item in trivia)
            {
                if (item.Kind == TriviaKind.LineComment || item.Kind == TriviaKind.BlockComment)
                    Add(result, snapshot, item.Span, _comment);
            }
        }

        /// <summary>
        /// token → 分类。
        ///
        /// <paramref name="previous"/> 是标识符归类的关键：PEVT 里 <c>@name</c> 的名称、
        /// <c>_name</c> 的块名和 <c>#label</c> 的标签都是普通 <c>IdentifierToken</c>，
        /// 只有看前一个 token 才知道它是哪一种。
        /// </summary>
        private IClassificationType? Classify(SyntaxToken token, SyntaxToken? previous, SyntaxToken? next)
        {
            switch (token.Kind)
            {
                case SyntaxKind.IntegerLiteralToken:
                case SyntaxKind.FloatLiteralToken:
                    return _number;

                case SyntaxKind.StringLiteralToken:
                case SyntaxKind.CharLiteralToken:
                case SyntaxKind.EventIdLiteralToken:
                    return _string;

                case SyntaxKind.RawContentToken:
                    return _rawContent;

                case SyntaxKind.TripleQuoteToken:
                case SyntaxKind.DollarRawToken:
                    return _keyword;

                case SyntaxKind.IdentifierToken:
                    if (previous == null)
                        return null;
                    if (previous.Kind == SyntaxKind.AtToken)
                        return _builtinCall;
                    if (previous.Kind == SyntaxKind.HashToken)
                        return _label;
                    return token.Text.StartsWith("_", StringComparison.Ordinal) ? _blockName : null;

                case SyntaxKind.AtToken:
                    return next != null && next.Kind == SyntaxKind.IdentifierToken ? _builtinCall : _operator;

                case SyntaxKind.HashToken:
                    return next != null && next.Kind == SyntaxKind.IdentifierToken ? _label : _operator;

                case SyntaxKind.EndOfFileToken:
                case SyntaxKind.BadToken:
                case SyntaxKind.None:
                    return null;

                default:
                    return IsKeyword(token.Kind) ? _keyword : _operator;
            }
        }

        /// <summary>关键字 token 的范围就是 <see cref="SyntaxFacts.Keywords"/> 的值域。</summary>
        private static bool IsKeyword(SyntaxKind kind)
        {
            foreach (KeyValuePair<string, SyntaxKind> entry in SyntaxFacts.Keywords)
            {
                if (entry.Value == kind)
                    return true;
            }

            return false;
        }

        private static void Add(List<ClassificationSpan> result, ITextSnapshot snapshot, TextSpan span, IClassificationType type)
        {
            int start = Math.Max(0, Math.Min(span.Start, snapshot.Length));
            int end = Math.Max(start, Math.Min(span.End, snapshot.Length));
            if (end <= start)
                return;

            result.Add(new ClassificationSpan(new SnapshotSpan(snapshot, new Span(start, end - start)), type));
        }
    }
}
