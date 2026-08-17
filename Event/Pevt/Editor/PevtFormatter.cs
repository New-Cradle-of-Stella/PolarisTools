using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Text;
using Microsoft.VisualStudio.Commanding;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor.Commanding.Commands;
using Microsoft.VisualStudio.Utilities;

namespace PolarisTools.Event.Pevt.Editor
{
    /// <summary>
    /// 非强制格式化：嵌套四空格。
    ///
    /// "非强制"是计划的用词，含义是它只在作者主动请求时运行（格式化文档／格式化选定内容），
    /// 不在敲字、粘贴或保存时自动改写。PEVT 的缩进不参与语义，硬性自动格式化只会和作者的排版打架。
    ///
    /// 两件事绝对不能碰：
    ///   原始文本块 <c>'''...'''</c> 的内容——它按原文提交给原版解释器或 C# 编译器，改一个空格
    ///                                        就改了要执行的东西；
    ///   多行字符串的续行——列对齐是作者刻意排的，重排会破坏对齐。
    ///
    /// 因此这里是**纯文本行处理**而不是基于语法树的重写：只调整每一行的前导空白，行内一个字符都不动。
    /// 处于原始文本块或多行字符串内部的行整行跳过。
    /// </summary>
    internal static class PevtFormatter
    {
        public const int IndentWidth = 4;

        /// <summary>增加缩进的行首关键字。</summary>
        private static readonly string[] Openers =
        {
            "if ", "if\t", "while ", "while\t", "switch ", "switch\t", "block ", "block\t",
            "async block ", "elif ", "elif\t", "else", "case ", "case\t", "default",
        };

        /// <summary>减少缩进的行首关键字。</summary>
        private static readonly string[] Closers =
        {
            "endif", "endwhile", "endswitch", "endblock", "elif", "else", "case", "default",
        };

        /// <summary>
        /// 格式化整份源文本，返回新文本。换行风格沿用输入里出现的第一种。
        /// </summary>
        public static string Format(string text)
        {
            if (string.IsNullOrEmpty(text))
                return text;

            string newline = text.IndexOf("\r\n", StringComparison.Ordinal) >= 0 ? "\r\n" : "\n";
            string[] lines = text.Replace("\r\n", "\n").Split('\n');

            var result = new StringBuilder(text.Length + 64);
            int level = 0;
            bool inRawBlock = false;

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                string trimmed = line.Trim();

                if (inRawBlock)
                {
                    // 原始文本块内部：原样保留。只有闭合分隔符所在行让它退出。
                    result.Append(line);
                    if (CountTripleQuotes(line) % 2 == 1)
                        inRawBlock = false;
                    AppendNewLineIfNeeded(result, newline, i, lines.Length);
                    continue;
                }

                // 空行不加缩进，避免留下一行只有空格的"脏行"。
                if (trimmed.Length == 0)
                {
                    AppendNewLineIfNeeded(result, newline, i, lines.Length);
                    continue;
                }

                if (StartsWithAny(trimmed, Closers))
                    level = Math.Max(0, level - 1);

                result.Append(new string(' ', level * IndentWidth)).Append(trimmed);

                if (StartsWithAny(trimmed, Openers))
                    level++;

                // 本行打开了一个未闭合的原始文本块 → 后续行不再格式化。
                if (CountTripleQuotes(line) % 2 == 1)
                    inRawBlock = true;

                AppendNewLineIfNeeded(result, newline, i, lines.Length);
            }

            return result.ToString();
        }

        private static void AppendNewLineIfNeeded(StringBuilder builder, string newline, int index, int count)
        {
            if (index < count - 1)
                builder.Append(newline);
        }

        /// <summary>
        /// 数一行里的 <c>'''</c>。转义形式 <c>\'''</c> 不算——它是内容里的字面三引号，
        /// 把它算成分隔符会让"块从哪里结束"整段错位。
        /// </summary>
        private static int CountTripleQuotes(string line)
        {
            int count = 0;
            for (int i = 0; i + 2 < line.Length + 0; i++)
            {
                if (i + 2 >= line.Length)
                    break;
                if (line[i] != '\'' || line[i + 1] != '\'' || line[i + 2] != '\'')
                    continue;
                if (i > 0 && line[i - 1] == '\\')
                {
                    i += 2;
                    continue;
                }

                count++;
                i += 2;
            }

            return count;
        }

        private static bool StartsWithAny(string trimmed, string[] prefixes)
        {
            foreach (string prefix in prefixes)
            {
                if (!trimmed.StartsWith(prefix, StringComparison.Ordinal))
                    continue;

                // "else" / "default" / "endif" 这类无参数关键字要求整词匹配，
                // 否则 `defaultValue = 1` 会被误判成 case 分支。
                if (prefix.Length == trimmed.Length || !char.IsLetterOrDigit(trimmed[prefix.Length]))
                    return true;
            }

            return false;
        }
    }

    /// <summary>格式化文档 / 格式化选定内容。只有作者主动触发时才运行。</summary>
    [Export(typeof(ICommandHandler))]
    [ContentType(PevtContentType.Name)]
    [Name("PevtFormat")]
    internal sealed class PevtFormatCommandHandler
        : ICommandHandler<FormatDocumentCommandArgs>, ICommandHandler<FormatSelectionCommandArgs>
    {
        public string DisplayName => "格式化 PEVT";

        public CommandState GetCommandState(FormatDocumentCommandArgs args) => CommandState.Available;

        public CommandState GetCommandState(FormatSelectionCommandArgs args) => CommandState.Available;

        public bool ExecuteCommand(FormatDocumentCommandArgs args, CommandExecutionContext executionContext) =>
            FormatWholeDocument(args.SubjectBuffer);

        /// <summary>
        /// 选定内容也整份格式化。
        ///
        /// 缩进层级由文档从头累计而来，只重排选区会得到一个和上下文不一致的层级；
        /// 与其给出错误的局部结果，不如按整份处理——反正行内内容一个字符都不改。
        /// </summary>
        public bool ExecuteCommand(FormatSelectionCommandArgs args, CommandExecutionContext executionContext) =>
            FormatWholeDocument(args.SubjectBuffer);

        private static bool FormatWholeDocument(ITextBuffer buffer)
        {
            try
            {
                ITextSnapshot snapshot = buffer.CurrentSnapshot;
                string original = snapshot.GetText();
                string formatted = PevtFormatter.Format(original);

                if (string.Equals(original, formatted, StringComparison.Ordinal))
                    return true;

                using (ITextEdit edit = buffer.CreateEdit())
                {
                    edit.Replace(0, snapshot.Length, formatted);
                    edit.Apply();
                }

                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
