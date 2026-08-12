using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion.Data;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Polaris.Event.Compiler.Aliases;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace PolarisTools.Event.Language
{
    /// <summary>
    /// 阶段5 §8：命令/角色/姿势/位置/音效/事件ID/标签自动补全。全部基于"光标所在行的纯文本前缀"做
    /// 正则识别上下文，不依赖完整 parser——补全只需要"大概率对"，误判的成本是"没弹出建议"，
    /// 比在编辑过程中維护一份实时 AST 便宜得多。
    /// </summary>
    internal sealed class HppCompletionSource : IAsyncCompletionSource
    {
        static readonly string[] CommandNames = { "char", "wait", "sfx", "set", "if", "else", "goto", "call", "return", "raw" };

        static readonly Regex CommandPrefix = new Regex(@"^\s*@(?<partial>\w*)$", RegexOptions.Compiled);
        static readonly Regex PosePrefix = new Regex(@"(?<actor>[A-Za-z_][A-Za-z0-9_]*)\.(?<partial>\w*)$", RegexOptions.Compiled);
        static readonly Regex ActorLineStart = new Regex(@"^\s*(?<partial>[A-Za-z_]\w*)$", RegexOptions.Compiled);
        static readonly Regex CharMainArg = new Regex(@"^\s*@char\s+(?<partial>[A-Za-z_]\w*)$", RegexOptions.Compiled);
        static readonly Regex PositionPrefix = new Regex(@"\bpos:(?<partial>\w*)$", RegexOptions.Compiled);
        static readonly Regex SfxPrefix = new Regex(@"^\s*@sfx\s+(?<partial>\w*)$", RegexOptions.Compiled);
        static readonly Regex CallPrefix = new Regex(@"^\s*@call\s+(?<partial>\w*)$", RegexOptions.Compiled);
        static readonly Regex GotoPrefix = new Regex(@"^\s*@goto\s+#(?<partial>\w*)$", RegexOptions.Compiled);
        static readonly Regex LabelDefinition = new Regex(@"^\s*#(?<name>.+)$", RegexOptions.Compiled);

        readonly ITextView textView;
        readonly string filePath;

        public HppCompletionSource(ITextView textView, string filePath)
        {
            this.textView = textView;
            this.filePath = filePath;
        }

        public CompletionStartData InitializeCompletion(CompletionTrigger trigger, SnapshotPoint triggerLocation, CancellationToken token)
        {
            var line = triggerLocation.GetContainingLine();
            string text = line.GetText();
            int offset = triggerLocation.Position - line.Start.Position;
            var (start, length) = FindWordBounds(text, offset);

            // "@" 不算 IsWordChar，FindWordBounds 找到的应用范围不会把它包进去；但命令补全项的
            // DisplayText 本身就带着 "@"（见下面 CommandNames.Select(c => MakeItem("@" + c))），
            // 提交时只替换"@"后面的那段词，原来敲的"@"会原样留着，变成"@@char"。这里把已经键入的
            // "@" 一并纳入替换范围，提交时整段一起换成"@char"就不会重复了。
            if (start > 0 && text[start - 1] == '@')
            {
                start -= 1;
                length += 1;
            }

            return new CompletionStartData(CompletionParticipation.ProvidesItems, new SnapshotSpan(line.Start + start, length));
        }

        public Task<CompletionContext> GetCompletionContextAsync(
            IAsyncCompletionSession session, CompletionTrigger trigger, SnapshotPoint triggerLocation, SnapshotSpan applicableToSpan, CancellationToken token)
        {
            var line = triggerLocation.GetContainingLine();
            string lineText = line.GetText();
            int offset = Math.Min(triggerLocation.Position - line.Start.Position, lineText.Length);
            string prefix = lineText.Substring(0, offset);

            var items = new List<CompletionItem>();

            Match m;
            if ((m = CommandPrefix.Match(prefix)).Success)
            {
                items.AddRange(CommandNames.Select(c => MakeItem("@" + c)));
            }
            else if ((m = GotoPrefix.Match(prefix)).Success)
            {
                items.AddRange(CollectLabels().Select(MakeItem));
            }
            else if ((m = SfxPrefix.Match(prefix)).Success)
            {
                var aliases = LoadAliases();
                if (aliases != null)
                {
                    items.AddRange(aliases.Audio.Sfx.Keys.Select(MakeItem));
                }
            }
            else if ((m = CallPrefix.Match(prefix)).Success)
            {
                var aliases = LoadAliases();
                if (aliases != null)
                {
                    items.AddRange(aliases.Events.Keys.Select(MakeItem));
                }
            }
            else if ((m = PositionPrefix.Match(prefix)).Success)
            {
                var aliases = LoadAliases();
                if (aliases != null)
                {
                    items.AddRange(aliases.Positions.Keys.Select(MakeItem));
                }
            }
            else if ((m = PosePrefix.Match(prefix)).Success)
            {
                var aliases = LoadAliases();
                if (aliases != null && aliases.Actors.TryGetValue(m.Groups["actor"].Value, out var actor))
                {
                    items.AddRange(actor.Poses.Keys.Select(MakeItem));
                }
            }
            else if (CharMainArg.IsMatch(prefix) || ActorLineStart.IsMatch(prefix))
            {
                var aliases = LoadAliases();
                if (aliases != null)
                {
                    items.AddRange(aliases.Actors.Keys.Select(MakeItem));
                }
            }

            return Task.FromResult(new CompletionContext(items.ToImmutableArray()));
        }

        public Task<object> GetDescriptionAsync(IAsyncCompletionSession session, CompletionItem item, CancellationToken token)
        {
            return Task.FromResult<object>(item.DisplayText);
        }

        CompletionItem MakeItem(string text) => new CompletionItem(text, this);

        IEnumerable<string> CollectLabels()
        {
            var snapshot = textView.TextBuffer.CurrentSnapshot;
            for (int i = 0; i < snapshot.LineCount; i++)
            {
                var text = snapshot.GetLineFromLineNumber(i).GetText();
                var match = LabelDefinition.Match(text.TrimStart());
                if (match.Success)
                {
                    yield return match.Groups["name"].Value.Trim();
                }
            }
        }

        AliasDocument LoadAliases()
        {
            string directory = string.IsNullOrEmpty(filePath) ? null : Path.GetDirectoryName(filePath);
            return HppAliasFileLocator.FindAliasDocument(directory, out _);
        }

        static (int start, int length) FindWordBounds(string text, int offset)
        {
            bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';

            int s = Math.Min(offset, text.Length);
            while (s > 0 && IsWordChar(text[s - 1]))
            {
                s--;
            }

            int e = Math.Min(offset, text.Length);
            while (e < text.Length && IsWordChar(text[e]))
            {
                e++;
            }

            return (s, e - s);
        }
    }
}
