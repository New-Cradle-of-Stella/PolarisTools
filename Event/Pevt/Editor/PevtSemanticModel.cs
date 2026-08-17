using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Polaris.Pevt.Actors;
using Polaris.Pevt.Binding;
using Polaris.Pevt.Commands;
using Polaris.Pevt.Diagnostics;
using Polaris.Pevt.Syntax;
using Polaris.Pevt.Text;

namespace PolarisTools.Event.Pevt.Editor
{
    /// <summary>一个可导航符号的种类。</summary>
    internal enum PevtSymbolKind
    {
        Variable,
        Constant,
        Handler,
        Block,
        Label,
        BuiltinCall,
        ActorId,
        AppearanceId,
        AnchorId,
        EventId,
    }

    /// <summary>源码里一处符号出现（声明或引用）。</summary>
    internal sealed class PevtSymbolOccurrence
    {
        public PevtSymbolKind Kind { get; }

        public string Name { get; }

        public TextSpan Span { get; }

        /// <summary>是否是声明处。跳转定义找的就是它。</summary>
        public bool IsDeclaration { get; }

        /// <summary>变量/常量/形参的声明类型；其余种类为 null。</summary>
        public PevtType? DeclaredType { get; }

        public PevtSymbolOccurrence(PevtSymbolKind kind, string name, TextSpan span, bool isDeclaration, PevtType? declaredType = null)
        {
            Kind = kind;
            Name = name;
            Span = span;
            IsDeclaration = isDeclaration;
            DeclaredType = declaredType;
        }
    }

    /// <summary>
    /// 编辑器侧的语义模型：一次词法 + 语法之后，把"文档里有哪些符号、各自出现在哪里"整理出来。
    ///
    /// 刻意不引用任何 Visual Studio 类型——补全、快速信息、跳转和引用高亮全部建在它上面，因此
    /// 这一层可以脱离编辑器单独验证，而编辑器侧只剩"把结果画出来"。
    ///
    /// 它**不是**第二个绑定器：类型判定、诊断编号、重载选择一律来自共享 Core。这里只做位置索引，
    /// 也就是 Core 不保存的那一件事——每个名字在源文本的哪些跨度上出现过。
    /// </summary>
    internal sealed class PevtSemanticModel
    {
        private PevtSemanticModel(
            SourceText source,
            DocumentSyntax? document,
            IReadOnlyList<SyntaxToken> tokens,
            IReadOnlyList<PevtSymbolOccurrence> occurrences,
            bool hasCsCapability,
            bool hasAsyncCapability)
        {
            Source = source;
            Document = document;
            Tokens = tokens;
            Occurrences = occurrences;
            HasCsCapability = hasCsCapability;
            HasAsyncCapability = hasAsyncCapability;
        }

        public SourceText Source { get; }

        public DocumentSyntax? Document { get; }

        public IReadOnlyList<SyntaxToken> Tokens { get; }

        public IReadOnlyList<PevtSymbolOccurrence> Occurrences { get; }

        public bool HasCsCapability { get; }

        public bool HasAsyncCapability { get; }

        /// <summary>解析一份编辑器文本。文本不可解码时返回 null。</summary>
        public static PevtSemanticModel? Create(string text, CancellationToken cancellationToken = default)
        {
            SourceText? source = PevtEditorText.Load(text, cancellationToken);
            if (source == null)
                return null;

            var bag = new DiagnosticBag();
            IReadOnlyList<SyntaxToken> tokens = Lexer.Tokenize(source, bag, cancellationToken);
            DocumentSyntax document = new Parser(tokens, bag, source).ParseDocument();

            bool cs = false;
            bool async = false;
            foreach (EnableDeclarationSyntax enable in document.EnableDeclarations)
            {
                string name = enable.Capability.Text;
                if (name == "cs")
                    cs = true;
                else if (name == "async")
                    async = true;
            }

            var occurrences = new List<PevtSymbolOccurrence>();
            CollectFromTokens(tokens, occurrences);
            CollectFromStatements(document.Statements, occurrences);

            return new PevtSemanticModel(source, document, tokens, occurrences, cs, async);
        }

        /// <summary>
        /// 从 token 流收集"只看形状就能确定"的出现：<c>@name</c>、<c>#label</c>、<c>_block</c>。
        ///
        /// 走 token 而不是语法树，是因为编辑中的文档大量处于语法不完整状态——用户正打到一半的
        /// <c>@say(</c> 在树上可能根本不成节点，但补全和快速信息必须在那一刻就工作。
        /// </summary>
        private static void CollectFromTokens(IReadOnlyList<SyntaxToken> tokens, List<PevtSymbolOccurrence> result)
        {
            for (int i = 1; i < tokens.Count; i++)
            {
                SyntaxToken token = tokens[i];
                if (token.IsMissing || token.Kind != SyntaxKind.IdentifierToken)
                    continue;

                SyntaxToken previous = tokens[i - 1];
                if (previous.Kind == SyntaxKind.AtToken)
                    result.Add(new PevtSymbolOccurrence(PevtSymbolKind.BuiltinCall, token.Text, token.Span, false));
                else if (previous.Kind == SyntaxKind.HashToken)
                    result.Add(new PevtSymbolOccurrence(PevtSymbolKind.Label, token.Text, token.Span, previous.Kind == SyntaxKind.HashToken));
            }
        }

        private static void CollectFromStatements(IReadOnlyList<StatementSyntax> statements, List<PevtSymbolOccurrence> result)
        {
            foreach (StatementSyntax statement in statements)
                CollectFromStatement(statement, result);
        }

        private static void CollectFromStatement(StatementSyntax statement, List<PevtSymbolOccurrence> result)
        {
            switch (statement)
            {
                case VariableDeclarationSyntax variable:
                    Declare(result, PevtSymbolKind.Variable, variable.Name, variable.Type);
                    CollectFromExpression(variable.Initializer, result);
                    return;

                case ConstantDeclarationSyntax constant:
                    Declare(result, PevtSymbolKind.Constant, constant.Name, constant.Type);
                    CollectFromExpression(constant.Initializer, result);
                    return;

                case HandlerDeclarationStatementSyntax handler:
                    if (!handler.Name.IsMissing)
                        result.Add(new PevtSymbolOccurrence(PevtSymbolKind.Handler, handler.Name.Text, handler.Name.Span, true));
                    CollectFromExpression(handler.Initializer, result);
                    return;

                case BlockDefinitionStatementSyntax block:
                    if (!block.Name.IsMissing)
                        result.Add(new PevtSymbolOccurrence(PevtSymbolKind.Block, block.Name.Text, block.Name.Span, true));
                    if (block.Parameters != null)
                    {
                        foreach (ParameterSyntax parameter in block.Parameters.Parameters)
                            Declare(result, PevtSymbolKind.Variable, parameter.Name, parameter.Type);
                    }

                    CollectFromStatements(block.Body, result);
                    return;

                case LabelStatementSyntax label:
                    if (!label.Name.IsMissing)
                        result.Add(new PevtSymbolOccurrence(PevtSymbolKind.Label, label.Name.Text, label.Name.Span, true));
                    return;

                case GotoLabelStatementSyntax gotoLabel:
                    if (!gotoLabel.Name.IsMissing)
                        result.Add(new PevtSymbolOccurrence(PevtSymbolKind.Label, gotoLabel.Name.Text, gotoLabel.Name.Span, false));
                    return;

                case AssignmentStatementSyntax assignment:
                    if (!assignment.Target.IsMissing)
                        result.Add(new PevtSymbolOccurrence(PevtSymbolKind.Variable, assignment.Target.Text, assignment.Target.Span, false));
                    CollectFromExpression(assignment.Value, result);
                    return;

                case KillStatementSyntax kill:
                    if (!kill.Handle.IsMissing)
                        result.Add(new PevtSymbolOccurrence(PevtSymbolKind.Handler, kill.Handle.Text, kill.Handle.Span, false));
                    return;

                case IfStatementSyntax ifStatement:
                    CollectFromExpression(ifStatement.Condition, result);
                    CollectFromStatements(ifStatement.Body, result);
                    foreach (ElifClauseSyntax elif in ifStatement.ElifClauses)
                    {
                        CollectFromExpression(elif.Condition, result);
                        CollectFromStatements(elif.Body, result);
                    }

                    if (ifStatement.ElseClause != null)
                        CollectFromStatements(ifStatement.ElseClause.Body, result);
                    return;

                case WhileStatementSyntax whileStatement:
                    CollectFromExpression(whileStatement.Condition, result);
                    CollectFromStatements(whileStatement.Body, result);
                    return;

                case SwitchStatementSyntax switchStatement:
                    CollectFromExpression(switchStatement.Value, result);
                    foreach (SwitchArmSyntax arm in switchStatement.Arms)
                    {
                        if (arm is CaseArmSyntax caseArm)
                            CollectFromExpression(caseArm.Value, result);
                        CollectFromStatements(arm.Body, result);
                    }

                    return;

                case ReturnStatementSyntax returnStatement:
                    if (returnStatement.Target != null && !returnStatement.Target.IsMissing)
                        result.Add(new PevtSymbolOccurrence(PevtSymbolKind.Variable, returnStatement.Target.Text, returnStatement.Target.Span, false));
                    return;

                case ExpressionStatementSyntax expressionStatement:
                    CollectFromExpression(expressionStatement.Expression, result);
                    return;
            }
        }

        private static void Declare(List<PevtSymbolOccurrence> result, PevtSymbolKind kind, SyntaxToken name, SyntaxToken type)
        {
            if (name.IsMissing)
                return;

            PevtType? declared = type.IsMissing ? (PevtType?)null : TryType(type.Kind);
            result.Add(new PevtSymbolOccurrence(kind, name.Text, name.Span, true, declared));
        }

        private static PevtType? TryType(SyntaxKind kind)
        {
            switch (kind)
            {
                case SyntaxKind.IntKeyword: return PevtType.Int;
                case SyntaxKind.FloatKeyword: return PevtType.Float;
                case SyntaxKind.BoolKeyword: return PevtType.Bool;
                case SyntaxKind.CharKeyword: return PevtType.Char;
                case SyntaxKind.StringKeyword: return PevtType.String;
                default: return null;
            }
        }

        private static void CollectFromExpression(ExpressionSyntax? expression, List<PevtSymbolOccurrence> result)
        {
            switch (expression)
            {
                case null:
                    return;

                case NameExpressionSyntax name:
                    if (!name.Identifier.IsMissing)
                        result.Add(new PevtSymbolOccurrence(PevtSymbolKind.Variable, name.Identifier.Text, name.Identifier.Span, false));
                    return;

                case ParenthesizedExpressionSyntax parenthesized:
                    CollectFromExpression(parenthesized.Inner, result);
                    return;

                case ConversionExpressionSyntax conversion:
                    if (!conversion.Variable.IsMissing)
                        result.Add(new PevtSymbolOccurrence(PevtSymbolKind.Variable, conversion.Variable.Text, conversion.Variable.Span, false));
                    return;

                case UnaryExpressionSyntax unary:
                    CollectFromExpression(unary.Operand, result);
                    return;

                case ChainedBinaryExpressionSyntax chain:
                    CollectFromExpression(chain.First, result);
                    foreach (BinaryChainSegment segment in chain.Segments)
                        CollectFromExpression(segment.Operand, result);
                    return;

                case BuiltinCallExpressionSyntax builtin:
                    foreach (ExpressionSyntax argument in builtin.Arguments.Arguments)
                        CollectFromExpression(argument, result);
                    return;

                case CustomBlockCallExpressionSyntax blockCall:
                    if (!blockCall.Name.IsMissing)
                        result.Add(new PevtSymbolOccurrence(PevtSymbolKind.Block, blockCall.Name.Text, blockCall.Name.Span, false));
                    foreach (ExpressionSyntax argument in blockCall.Arguments.Arguments)
                        CollectFromExpression(argument, result);
                    return;

                case AwaitExpressionSyntax await:
                    if (!await.Handle.IsMissing)
                        result.Add(new PevtSymbolOccurrence(PevtSymbolKind.Handler, await.Handle.Text, await.Handle.Span, false));
                    return;

                case StatusExpressionSyntax status:
                    if (!status.Handle.IsMissing)
                        result.Add(new PevtSymbolOccurrence(PevtSymbolKind.Handler, status.Handle.Text, status.Handle.Span, false));
                    return;
            }
        }

        // ---- 查询 ----

        /// <summary>命中 <paramref name="position"/> 的符号出现；没有时为 null。</summary>
        public PevtSymbolOccurrence? FindAt(int position)
        {
            foreach (PevtSymbolOccurrence occurrence in Occurrences)
            {
                if (position >= occurrence.Span.Start && position <= occurrence.Span.End)
                    return occurrence;
            }

            return null;
        }

        /// <summary>同名同类的全部出现。引用高亮与引用查找用它。</summary>
        public IReadOnlyList<PevtSymbolOccurrence> FindAll(PevtSymbolOccurrence target)
        {
            var result = new List<PevtSymbolOccurrence>();
            foreach (PevtSymbolOccurrence occurrence in Occurrences)
            {
                if (occurrence.Kind == target.Kind && string.Equals(occurrence.Name, target.Name, StringComparison.Ordinal))
                    result.Add(occurrence);
            }

            return result;
        }

        /// <summary>同名同类的声明处；没有声明（例如跨模组 actor、晚注册事件）时为 null。</summary>
        public PevtSymbolOccurrence? FindDeclaration(PevtSymbolOccurrence target)
        {
            foreach (PevtSymbolOccurrence occurrence in FindAll(target))
            {
                if (occurrence.IsDeclaration)
                    return occurrence;
            }

            return null;
        }

        /// <summary>光标位置之前（含）已经声明过的变量、常量、形参与句柄，按名字去重。</summary>
        public IReadOnlyList<PevtSymbolOccurrence> VisibleDeclarationsBefore(int position)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var result = new List<PevtSymbolOccurrence>();

            foreach (PevtSymbolOccurrence occurrence in Occurrences)
            {
                if (!occurrence.IsDeclaration || occurrence.Span.Start > position)
                    continue;
                if (occurrence.Kind == PevtSymbolKind.Label)
                    continue;
                if (seen.Add(occurrence.Kind + ":" + occurrence.Name))
                    result.Add(occurrence);
            }

            return result;
        }
    }

    /// <summary>
    /// 人物目录的编辑器视图：内置固定目录 + 当前项目里的 <c>.pactor</c>。
    ///
    /// 计划要求"人物补全合并内置目录与当前项目 .pactor"，同时"未知跨模组人物 ID 不是 .pevt 静态错误"
    /// ——因此这里只用于**补全和跳转**，一次都不产生诊断。
    /// </summary>
    internal static class PevtActorIndex
    {
        private static readonly object Gate = new object();
        private static readonly Dictionary<string, CacheEntry> Cache = new Dictionary<string, CacheEntry>(StringComparer.OrdinalIgnoreCase);

        private sealed class CacheEntry
        {
            public DateTime StampUtc;
            public ActorCatalog? Catalog;
        }

        /// <summary>内置 <c>aic</c> 目录。加载失败时为 null。</summary>
        public static ActorCatalog? Builtin
        {
            get
            {
                try
                {
                    return BuiltinActorCatalog.Catalog;
                }
                catch (Exception)
                {
                    return null;
                }
            }
        }

        /// <summary>读一个项目内的 <c>.pactor</c>，按文件写入时间缓存。</summary>
        public static ActorCatalog? Load(string path)
        {
            try
            {
                DateTime stamp = System.IO.File.GetLastWriteTimeUtc(path);
                lock (Gate)
                {
                    if (Cache.TryGetValue(path, out CacheEntry entry) && entry.StampUtc == stamp)
                        return entry.Catalog;
                }

                ActorCatalogReadResult result = ActorCatalogReader.ReadText(
                    System.IO.File.ReadAllText(path), path, ActorCatalogSourceKind.External);

                lock (Gate)
                {
                    Cache[path] = new CacheEntry { StampUtc = stamp, Catalog = result.Catalog };
                }

                return result.Catalog;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>内置目录与 <paramref name="projectDirectory"/> 下全部 <c>.pactor</c> 的合并结果。</summary>
        public static IReadOnlyList<ActorCatalog> Catalogs(string? projectDirectory)
        {
            var result = new List<ActorCatalog>();

            ActorCatalog? builtin = Builtin;
            if (builtin != null)
                result.Add(builtin);

            if (string.IsNullOrEmpty(projectDirectory) || !System.IO.Directory.Exists(projectDirectory))
                return result;

            try
            {
                foreach (string file in System.IO.Directory.EnumerateFiles(projectDirectory, "*.pactor", System.IO.SearchOption.AllDirectories))
                {
                    ActorCatalog? catalog = Load(file);
                    if (catalog != null)
                        result.Add(catalog);
                }
            }
            catch (Exception)
            {
                // 项目目录在编辑期间可能被移动或权限受限；补全少几项好过抛异常。
            }

            return result;
        }

        /// <summary>全部可见的人物最终 ID。</summary>
        public static IReadOnlyList<string> ActorIds(string? projectDirectory)
        {
            var result = new List<string>();
            foreach (ActorCatalog catalog in Catalogs(projectDirectory))
            {
                foreach (ActorDefinition actor in catalog.Actors)
                    result.Add(catalog.GetActorId(actor));
            }

            result.Sort(StringComparer.Ordinal);
            return result;
        }

        /// <summary>按最终 ID 找到人物及其所属目录。</summary>
        public static bool TryFind(string? projectDirectory, string actorId, out ActorCatalog catalog, out ActorDefinition actor)
        {
            foreach (ActorCatalog candidate in Catalogs(projectDirectory))
            {
                if (candidate.TryGetActor(actorId, out ActorDefinition found))
                {
                    catalog = candidate;
                    actor = found;
                    return true;
                }
            }

            catalog = null!;
            actor = null!;
            return false;
        }
    }

    /// <summary>
    /// <c>@</c> 调用的签名文本。补全描述与快速信息共用同一份渲染，避免两处各写一套格式。
    /// </summary>
    internal static class PevtSignatureText
    {
        public static string Describe(CommandDescriptor descriptor)
        {
            var builder = new System.Text.StringBuilder();
            if (descriptor.IsAsync)
                builder.Append("async ");

            builder.Append('@').Append(descriptor.Name).Append('(');
            for (int i = 0; i < descriptor.Parameters.Count; i++)
            {
                if (i > 0)
                    builder.Append(", ");
                CommandParameter parameter = descriptor.Parameters[i];
                builder.Append(parameter.Name).Append(" : ").Append(parameter.Type.DisplayName());
            }

            builder.Append(')');
            if (descriptor.ReturnType.HasValue)
                builder.Append(" : ").Append(descriptor.ReturnType.Value.DisplayName());

            return builder.ToString();
        }

        /// <summary>执行方式、能力要求与参数域，也就是作者真正需要知道的那几件事。</summary>
        public static string DescribeDetails(CommandDescriptor descriptor)
        {
            var lines = new List<string>();
            lines.Add("执行方式：" + WaitKindText(descriptor.WaitKind));
            lines.Add("优先级：" + descriptor.Priority);

            if (descriptor.CanRunInParallel)
                lines.Add("可并行：调用 @" + descriptor.StartName + " 立即返回 handler（需要 enable async）");

            if (descriptor.IsAsync)
                lines.Add("调用后立即返回 handler，需要文件声明 enable async");

            var domains = descriptor.Parameters
                .Where(p => p.Domain != null)
                .Select(p => p.Name + " ∈ " + DomainText(p.Domain))
                .ToList();
            if (domains.Count > 0)
                lines.Add("参数域：" + string.Join("；", domains));

            if (!string.IsNullOrEmpty(descriptor.Capability))
                lines.Add("能力标识：" + descriptor.Capability);

            return string.Join(Environment.NewLine, lines);
        }

        private static string DomainText(ParameterDomain domain) =>
            domain.ClosedValues != null && domain.ClosedValues.Count > 0
                ? domain.Name + " {" + string.Join(", ", domain.ClosedValues) + "}"
                : domain.Name;

        private static string WaitKindText(CommandWaitKind kind)
        {
            switch (kind)
            {
                case CommandWaitKind.Immediate: return "立即（当前解释步内完成）";
                case CommandWaitKind.Query: return "查询（立即完成并产生返回值）";
                case CommandWaitKind.Wait: return "等待（可跨帧，自动暂停当前流程）";
                case CommandWaitKind.WaitParallel: return "等待／可并行";
                default: return kind.ToString();
            }
        }
    }
}
