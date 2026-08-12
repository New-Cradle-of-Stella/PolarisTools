using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Classification;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace PolarisTools.Event.Language
{
    /// <summary>
    /// 逐行、纯正则的 .phxx lexer 高亮，刻意不复用 <c>Polaris.Event.Compiler</c> 的 parser/AST——
    /// 高亮必须在每次键入时都足够快（实现计划 §6.2），语义相关的错误由 <see cref="HppDiagnosticTagger"/>
    /// 异步补上。行为需要和 <c>HxxLexer</c>/<c>HxxParser</c> 认的语法保持"看起来一致"，但不要求
    /// 逐字节复用同一份代码——两者本就是两条独立的目的（高亮 vs. 编译）。
    /// </summary>
    internal sealed class HppClassifier : IClassifier
    {
        static readonly Regex DialoguePrefix = new Regex(
            @"^(?<actor>[A-Za-z_][A-Za-z0-9_]*)(\.(?<pose>[A-Za-z_][A-Za-z0-9_]*))?:",
            RegexOptions.Compiled);

        static readonly Regex CommandName = new Regex(@"^@(?<name>[A-Za-z_][A-Za-z0-9_]*)", RegexOptions.Compiled);

        static readonly Regex Variable = new Regex(@"\{[^{}\r\n]*\}", RegexOptions.Compiled);

        static readonly Regex Number = new Regex(@"^-?\d+(\.\d+)?$", RegexOptions.Compiled);

        readonly ITextBuffer buffer;
        readonly IClassificationTypeRegistryService registry;

        public HppClassifier(ITextBuffer buffer, IClassificationTypeRegistryService registry)
        {
            this.buffer = buffer;
            this.registry = registry;
        }

        public event EventHandler<ClassificationChangedEventArgs> ClassificationChanged;

        public IList<ClassificationSpan> GetClassificationSpans(SnapshotSpan span)
        {
            var result = new List<ClassificationSpan>();
            var snapshot = span.Snapshot;

            int startLine = snapshot.GetLineNumberFromPosition(span.Start);
            int endLine = snapshot.GetLineNumberFromPosition(span.End);

            for (int i = startLine; i <= endLine; i++)
            {
                ClassifyLine(snapshot.GetLineFromLineNumber(i), result);
            }

            return result;
        }

        void ClassifyLine(ITextSnapshotLine line, List<ClassificationSpan> result)
        {
            string text = line.GetText();
            int indent = 0;
            while (indent < text.Length && (text[indent] == ' ' || text[indent] == '\t'))
            {
                indent++;
            }

            string content = text.Substring(indent);
            int contentStart = line.Start.Position + indent;

            if (content.Length == 0)
            {
                return;
            }

            if (content[0] == ';')
            {
                Add(result, line.Snapshot, contentStart, content.Length, HppClassificationTypeNames.Comment);
                return;
            }

            if (content[0] == '#')
            {
                Add(result, line.Snapshot, contentStart, content.Length, HppClassificationTypeNames.Label);
                return;
            }

            if (content[0] == '@')
            {
                var cmd = CommandName.Match(content);
                if (cmd.Success)
                {
                    Add(result, line.Snapshot, contentStart, cmd.Length, HppClassificationTypeNames.Keyword);
                    ClassifyArguments(content, cmd.Length, contentStart, line.Snapshot, result);
                }

                return;
            }

            var dialogue = DialoguePrefix.Match(content);
            if (dialogue.Success)
            {
                var actorGroup = dialogue.Groups["actor"];
                Add(result, line.Snapshot, contentStart + actorGroup.Index, actorGroup.Length, HppClassificationTypeNames.Actor);

                var poseGroup = dialogue.Groups["pose"];
                if (poseGroup.Success)
                {
                    Add(result, line.Snapshot, contentStart + poseGroup.Index, poseGroup.Length, HppClassificationTypeNames.Pose);
                }
            }

            ClassifyVariables(content, contentStart, line.Snapshot, result);
        }

        void ClassifyArguments(string content, int fromIndex, int contentStart, ITextSnapshot snapshot, List<ClassificationSpan> result)
        {
            int i = fromIndex;
            while (i < content.Length)
            {
                while (i < content.Length && char.IsWhiteSpace(content[i]))
                {
                    i++;
                }

                if (i >= content.Length)
                {
                    break;
                }

                if (content[i] == '"')
                {
                    int start = i;
                    i++;
                    while (i < content.Length && content[i] != '"')
                    {
                        i++;
                    }

                    if (i < content.Length)
                    {
                        i++;
                    }

                    Add(result, snapshot, contentStart + start, i - start, HppClassificationTypeNames.String);
                    continue;
                }

                int tokenStart = i;
                while (i < content.Length && !char.IsWhiteSpace(content[i]))
                {
                    i++;
                }

                string token = content.Substring(tokenStart, i - tokenStart);
                ClassifyToken(token, tokenStart, contentStart, snapshot, result);
            }
        }

        void ClassifyToken(string token, int tokenStart, int contentStart, ITextSnapshot snapshot, List<ClassificationSpan> result)
        {
            int colon = token.IndexOf(':');
            if (token.Length > 1 && token[token.Length - 1] == '!' && colon < 0)
            {
                Add(result, snapshot, contentStart + tokenStart, token.Length, HppClassificationTypeNames.Flag);
                return;
            }

            if (colon > 0)
            {
                Add(result, snapshot, contentStart + tokenStart, colon, HppClassificationTypeNames.ParamName);

                string value = token.Substring(colon + 1);
                int valueStart = tokenStart + colon + 1;
                if (value.Length > 0)
                {
                    if (Number.IsMatch(value))
                    {
                        Add(result, snapshot, contentStart + valueStart, value.Length, HppClassificationTypeNames.Number);
                    }
                    else
                    {
                        ClassifyVariablesInRange(value, contentStart + valueStart, snapshot, result);
                    }
                }

                return;
            }

            if (Number.IsMatch(token))
            {
                Add(result, snapshot, contentStart + tokenStart, token.Length, HppClassificationTypeNames.Number);
            }
        }

        void ClassifyVariables(string content, int contentStart, ITextSnapshot snapshot, List<ClassificationSpan> result)
        {
            ClassifyVariablesInRange(content, contentStart, snapshot, result);
        }

        void ClassifyVariablesInRange(string text, int offset, ITextSnapshot snapshot, List<ClassificationSpan> result)
        {
            foreach (Match m in Variable.Matches(text))
            {
                Add(result, snapshot, offset + m.Index, m.Length, HppClassificationTypeNames.Variable);
            }
        }

        void Add(List<ClassificationSpan> result, ITextSnapshot snapshot, int start, int length, string classificationTypeName)
        {
            if (length <= 0)
            {
                return;
            }

            var type = registry.GetClassificationType(classificationTypeName);
            result.Add(new ClassificationSpan(new SnapshotSpan(snapshot, start, length), type));
        }
    }
}
