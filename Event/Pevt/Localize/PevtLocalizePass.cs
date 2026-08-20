using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Polaris.Lang;
using Polaris.Localization;
using Polaris.Pevt.Binding;
using Polaris.Pevt.Commands;
using Polaris.Pevt.Syntax;
using Polaris.Pevt.Text;

namespace PolarisTools.Event.Pevt.Localize;

/// <summary>源码里一处待抽取的显示文案。</summary>
internal sealed class PevtTextOccurrence
{
    public PevtTextOccurrence(string commandName, string parameterName, TextSpan span, string value, int line)
    {
        CommandName = commandName;
        ParameterName = parameterName;
        Span = span;
        Value = value;
        Line = line;
    }

    /// <summary>所在 <c>@</c> 的名字，只进 <c>.plang</c> 的说明列。</summary>
    public string CommandName { get; }

    public string ParameterName { get; }

    /// <summary>整个字符串字面量记号的跨度，包含两侧引号与多行续接段。</summary>
    public TextSpan Span { get; }

    /// <summary>已解码的文案（转义序列已还原、多行续接段以 <c>\n</c> 相连）。</summary>
    public string Value { get; }

    /// <summary>1 起的行号，只进说明列。</summary>
    public int Line { get; }
}

/// <summary>一处文案被替换成了哪个键。</summary>
internal sealed class PevtLocalizeReplacement
{
    public PevtLocalizeReplacement(PevtTextOccurrence occurrence, string key, bool reusedExistingKey)
    {
        Occurrence = occurrence;
        Key = key;
        ReusedExistingKey = reusedExistingKey;
    }

    public PevtTextOccurrence Occurrence { get; }

    public string Key { get; }

    /// <summary><c>.plang</c> 里已经有一条同文案的记录，这次直接复用了它的键。</summary>
    public bool ReusedExistingKey { get; }
}

/// <summary>一次「快速本地化」算出来的全部改动。执行之前不碰任何文件。</summary>
internal sealed class PevtLocalizePlan
{
    public PevtLocalizePlan(
        IReadOnlyList<PevtLocalizeReplacement> replacements,
        IReadOnlyList<PevtTextOccurrence> alreadyLocalized,
        PlangDocument document,
        int addedEntryCount,
        bool addedLanguage)
    {
        Replacements = replacements;
        AlreadyLocalized = alreadyLocalized;
        Document = document;
        AddedEntryCount = addedEntryCount;
        AddedLanguage = addedLanguage;
    }

    /// <summary>要替换的文案，按源码位置升序。写回缓冲区时必须倒着做，否则前一处替换会挪动后一处的跨度。</summary>
    public IReadOnlyList<PevtLocalizeReplacement> Replacements { get; }

    /// <summary>本来就已经是 <c>&amp;</c> 键、这次跳过的那些。只用于汇报。</summary>
    public IReadOnlyList<PevtTextOccurrence> AlreadyLocalized { get; }

    /// <summary>更新过的 <c>.plang</c> 文档（含原有内容）。</summary>
    public PlangDocument Document { get; }

    /// <summary>这次新增的条目数；复用已有键的那些不计入。</summary>
    public int AddedEntryCount { get; }

    /// <summary>这次往 <c>.plang</c> 里补了一门新语言。</summary>
    public bool AddedLanguage { get; }

    public bool IsEmpty => Replacements.Count == 0 && !AddedLanguage;
}

/// <summary>
/// 「快速本地化」的全部判断逻辑：哪些字面量是给玩家看的文案、各自分到什么键、<c>.plang</c> 该长什么样。
///
/// 刻意不引用任何 Visual Studio 类型，也不写文件——编辑器那一侧只负责把 <see cref="PevtLocalizePlan"/>
/// 落到缓冲区和磁盘上。"哪些参数是文案"更不在这里判断：它由共享 Core 的
/// <see cref="ParameterDomain.Text"/> 声明，游戏侧运行期解析用的是同一份声明，因此工具替换掉的那些位置
/// 与游戏真正会去查表的那些位置不可能对不上。
/// </summary>
internal static class PevtLocalizePass
{
    /// <summary>生成键时的序号宽度：<c>ForestLordDefeat.001</c>。</summary>
    private const string SequenceFormat = "000";

    /// <summary>
    /// 找出文档里全部处于文案位置的字符串字面量，按源码位置升序。
    /// </summary>
    public static IReadOnlyList<PevtTextOccurrence> Collect(DocumentSyntax document, SourceText source)
    {
        var result = new List<PevtTextOccurrence>();
        if (document == null)
            return result;

        foreach (StatementSyntax statement in document.Statements)
            CollectFromStatement(statement, source, result);

        result.Sort((a, b) => a.Span.Start.CompareTo(b.Span.Start));
        return result;
    }

    /// <summary>
    /// 把收集到的文案排进 <paramref name="existing"/>（可以为 null，表示 <c>.plang</c> 还不存在）。
    /// </summary>
    /// <param name="keyPrefix">键的前缀，通常是事件 ID。</param>
    /// <param name="languageCode">这些文案当前是用哪门语言写的。</param>
    /// <param name="languageName">该语言的显示名，只在 <c>.plang</c> 里新建这一列时用到。</param>
    public static PevtLocalizePlan Plan(
        IReadOnlyList<PevtTextOccurrence> occurrences,
        PlangDocument? existing,
        string keyPrefix,
        string languageCode,
        string languageName)
    {
        PlangDocument document = existing ?? new PlangDocument();

        bool addedLanguage = false;
        if (!string.IsNullOrEmpty(languageCode)
            && !document.Languages.Any(l => string.Equals(l.Code, languageCode, StringComparison.OrdinalIgnoreCase)))
        {
            document.Languages.Add(new PlangLanguage
            {
                Code = languageCode,
                DisplayName = string.IsNullOrWhiteSpace(languageName) ? languageCode : languageName,
                Enabled = true,
            });
            addedLanguage = true;
        }

        // 同一段文案在文件里出现多次时共用一个键——重复的台词没有理由让译者翻两遍。
        var byValue = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (PlangEntry entry in document.Entries)
        {
            if (!string.IsNullOrEmpty(entry.Key) && !string.IsNullOrEmpty(entry.NeutralValue))
                byValue[entry.NeutralValue] = entry.Key;
        }

        var usedKeys = new HashSet<string>(document.Entries.Select(e => e.Key ?? ""), StringComparer.Ordinal);
        int nextSequence = NextSequence(usedKeys, keyPrefix);

        var replacements = new List<PevtLocalizeReplacement>();
        var alreadyLocalized = new List<PevtTextOccurrence>();
        int added = 0;

        foreach (PevtTextOccurrence occurrence in occurrences)
        {
            // 已经是键的原样留着：重跑一次「快速本地化」不该把 `&mymod.001` 再包一层。
            if (LocalizedString.TryGetKey(occurrence.Value, out string _))
            {
                alreadyLocalized.Add(occurrence);
                continue;
            }

            // `&&` 开头是转义过的字面 `&`，进表的应该是玩家真正看到的那一份。
            string value = LocalizedString.Unescape(occurrence.Value);

            if (byValue.TryGetValue(value, out string existingKey))
            {
                replacements.Add(new PevtLocalizeReplacement(occurrence, existingKey, reusedExistingKey: true));
                continue;
            }

            string key;
            do
            {
                key = keyPrefix + "." + nextSequence.ToString(SequenceFormat, CultureInfo.InvariantCulture);
                nextSequence++;
            }
            while (!usedKeys.Add(key));

            var entry = new PlangEntry(key, value, $"@{occurrence.CommandName}.{occurrence.ParameterName} @ line {occurrence.Line}");
            if (!string.IsNullOrEmpty(languageCode))
                entry.Values[languageCode] = value;

            document.Entries.Add(entry);
            byValue[value] = key;
            added++;

            replacements.Add(new PevtLocalizeReplacement(occurrence, key, reusedExistingKey: false));
        }

        return new PevtLocalizePlan(replacements, alreadyLocalized, document, added, addedLanguage);
    }

    /// <summary>
    /// 替换文本：<c>"&amp;key"</c>。键由本方法生成，只含标识符字符与 <c>.</c>，不需要转义。
    /// </summary>
    public static string ReplacementLiteral(string key) => "\"" + LocalizedString.Sigil + key + "\"";

    /// <summary>
    /// 键前缀：优先用文件头 <c>id "..."</c> 声明的事件 ID，它天然带着模组自己的命名空间；
    /// 没有（或还没打完）时退回文件名。<c>.plang</c> 的键在整个进程里是全局的，重名会被
    /// <c>PlangConflictGuard</c> 判成跨模组致命冲突，所以绝不能退回成 <c>text.001</c> 这种通名。
    /// </summary>
    public static string KeyPrefix(DocumentSyntax document, string fileNameWithoutExtension)
    {
        string? id = document?.IdDeclaration?.Value.Value.Kind == TokenValueKind.String
            ? document.IdDeclaration.Value.Value.AsString
            : null;

        string prefix = Sanitize(string.IsNullOrWhiteSpace(id) ? fileNameWithoutExtension : id!);
        return string.IsNullOrEmpty(prefix) ? "pevt" : prefix;
    }

    /// <summary>键里只留标识符字符、<c>.</c> 与 <c>:</c>，其余折成 <c>_</c>。</summary>
    private static string Sanitize(string value)
    {
        if (string.IsNullOrEmpty(value))
            return "";

        var builder = new System.Text.StringBuilder(value.Length);
        foreach (char c in value)
            builder.Append(char.IsLetterOrDigit(c) || c == '.' || c == ':' || c == '_' || c == '-' ? c : '_');

        return builder.ToString().Trim('.', '_');
    }

    /// <summary>接着已有的 <c>&lt;前缀&gt;.NNN</c> 往下编号，不从 1 重来撞上已经发出去的键。</summary>
    private static int NextSequence(IEnumerable<string> usedKeys, string keyPrefix)
    {
        string prefix = keyPrefix + ".";
        int next = 1;

        foreach (string key in usedKeys)
        {
            if (key == null || !key.StartsWith(prefix, StringComparison.Ordinal))
                continue;

            if (int.TryParse(key.Substring(prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out int sequence)
                && sequence >= next)
            {
                next = sequence + 1;
            }
        }

        return next;
    }

    // ---- 语法遍历 ----

    private static void CollectFromStatement(StatementSyntax statement, SourceText source, List<PevtTextOccurrence> result)
    {
        switch (statement)
        {
            case VariableDeclarationSyntax variable:
                CollectFromExpression(variable.Initializer, source, result);
                return;

            case ConstantDeclarationSyntax constant:
                CollectFromExpression(constant.Initializer, source, result);
                return;

            case HandlerDeclarationStatementSyntax handler:
                CollectFromExpression(handler.Initializer, source, result);
                return;

            case BlockDefinitionStatementSyntax block:
                foreach (StatementSyntax inner in block.Body)
                    CollectFromStatement(inner, source, result);
                return;

            case AssignmentStatementSyntax assignment:
                CollectFromExpression(assignment.Value, source, result);
                return;

            case IfStatementSyntax ifStatement:
                CollectFromExpression(ifStatement.Condition, source, result);
                foreach (StatementSyntax inner in ifStatement.Body)
                    CollectFromStatement(inner, source, result);
                foreach (ElifClauseSyntax elif in ifStatement.ElifClauses)
                {
                    CollectFromExpression(elif.Condition, source, result);
                    foreach (StatementSyntax inner in elif.Body)
                        CollectFromStatement(inner, source, result);
                }

                if (ifStatement.ElseClause != null)
                {
                    foreach (StatementSyntax inner in ifStatement.ElseClause.Body)
                        CollectFromStatement(inner, source, result);
                }

                return;

            case WhileStatementSyntax whileStatement:
                CollectFromExpression(whileStatement.Condition, source, result);
                foreach (StatementSyntax inner in whileStatement.Body)
                    CollectFromStatement(inner, source, result);
                return;

            case SwitchStatementSyntax switchStatement:
                CollectFromExpression(switchStatement.Value, source, result);
                foreach (SwitchArmSyntax arm in switchStatement.Arms)
                {
                    if (arm is CaseArmSyntax caseArm)
                        CollectFromExpression(caseArm.Value, source, result);
                    foreach (StatementSyntax inner in arm.Body)
                        CollectFromStatement(inner, source, result);
                }

                return;

            case ExpressionStatementSyntax expressionStatement:
                CollectFromExpression(expressionStatement.Expression, source, result);
                return;
        }
    }

    private static void CollectFromExpression(ExpressionSyntax? expression, SourceText source, List<PevtTextOccurrence> result)
    {
        switch (expression)
        {
            case null:
                return;

            case ParenthesizedExpressionSyntax parenthesized:
                CollectFromExpression(parenthesized.Inner, source, result);
                return;

            case UnaryExpressionSyntax unary:
                CollectFromExpression(unary.Operand, source, result);
                return;

            case ChainedBinaryExpressionSyntax chain:
                CollectFromExpression(chain.First, source, result);
                foreach (BinaryChainSegment segment in chain.Segments)
                    CollectFromExpression(segment.Operand, source, result);
                return;

            case CustomBlockCallExpressionSyntax blockCall:
                // 自定义事件块的形参没有参数域，块正文里那句 `@say(text)` 才是文案位置。
                foreach (ExpressionSyntax argument in blockCall.Arguments.Arguments)
                    CollectFromExpression(argument, source, result);
                return;

            case BuiltinCallExpressionSyntax builtin:
                CollectFromBuiltinCall(builtin, source, result);
                return;
        }
    }

    private static void CollectFromBuiltinCall(BuiltinCallExpressionSyntax call, SourceText source, List<PevtTextOccurrence> result)
    {
        IReadOnlyList<ExpressionSyntax> arguments = call.Arguments.Arguments;

        for (int i = 0; i < arguments.Count; i++)
        {
            // 先递归：嵌套调用里的文案同样要抽。
            CollectFromExpression(arguments[i], source, result);

            if (call.Name.IsMissing || !TryGetTextParameterName(call.Name.Text, i, out string? parameterName))
                continue;

            // 只动字面量。变量或表达式拼出来的文本没有"原文"可以填进表格，静默改掉反而会写坏脚本。
            if (!(arguments[i] is LiteralExpressionSyntax literal)
                || literal.Token.IsMissing
                || literal.Token.Kind != SyntaxKind.StringLiteralToken
                || literal.Token.Value.Kind != TokenValueKind.String)
            {
                continue;
            }

            result.Add(new PevtTextOccurrence(
                call.Name.Text,
                parameterName!,
                literal.Token.Span,
                literal.Token.Value.AsString,
                source.GetLocation(literal.Token.Span).StartLine));
        }
    }

    /// <summary>
    /// <c>@名</c> 的第 <paramref name="index"/> 个实参是不是文案位置。
    ///
    /// 不做重载决议：编辑器里的文档随时可能类型不完整，而"第 i 个参数是不是文案"在同名重载之间
    /// 从来不冲突（<c>@choose</c> 的三个重载里前几位都是文案）。因此要求**全部**覆盖到这一位的重载
    /// 都把它标成文案，有一个不是就放过——宁可少抽一条，也不要把 style/key 这类 ID 换成本地化键。
    /// </summary>
    private static bool TryGetTextParameterName(string commandName, int index, out string? parameterName)
    {
        parameterName = null;

        IReadOnlyList<CommandDescriptor> overloads = CommandDescriptorCatalog.Builtin.Find(commandName);
        if (overloads.Count == 0)
            return false;

        bool sawParameter = false;
        foreach (CommandDescriptor descriptor in overloads)
        {
            if (index >= descriptor.Parameters.Count)
                continue;

            CommandParameter parameter = descriptor.Parameters[index];
            if (!ReferenceEquals(parameter.Domain, ParameterDomain.Text))
                return false;

            sawParameter = true;
            parameterName = parameter.Name;
        }

        return sawParameter;
    }
}
