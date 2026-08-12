using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Text;
using Polaris.Event.Compiler.Aliases;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace PolarisTools.Event.Language
{
    /// <summary>
    /// 阶段5 §8 提到的 Quick Info：悬停在 <c>@command</c> 上给出参数说明，悬停在
    /// <c>Actor</c>/<c>Actor.Pose</c> 上给出别名解析出的底层原始值——都是只读展示，不含跳转，
    /// 跳转部分是 <see cref="HppGotoDefinitionCommandFilter"/> 的职责。
    /// </summary>
    internal sealed class HppQuickInfoSource : IAsyncQuickInfoSource
    {
        static readonly Dictionary<string, string> CommandDocs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["char"] = "@char Actor[.Pose] [pos:position] — 切换/显示说话人，可选立绘姿势和站位。",
            ["wait"] = "@wait <frames> — 等待指定帧数（非负整数）。",
            ["sfx"] = "@sfx <alias> — 播放别名文件 audio.sfx 下定义的音效。",
            ["set"] = "@set name = value（也支持 +=/-=）— 设置变量。",
            ["if"] = "@if <expr> — 条件分支，配合缩进块和可选的 @else 使用。",
            ["else"] = "@else — 配合同缩进的 @if 使用。",
            ["goto"] = "@goto #Label [if:expr] — 跳转到本文件内的标签。",
            ["call"] = "@call <eventAlias> [args:a,b,c] — 调用别名文件 events 下定义的另一个事件。",
            ["return"] = "@return — 结束当前事件。",
            ["raw"] = "@raw \"...\" — 原样插入一行底层哈语言，跳过别名解析和大部分诊断。",
        };

        static readonly Regex DialoguePrefix = new Regex(
            @"^\s*(?<actor>[A-Za-z_][A-Za-z0-9_]*)(\.(?<pose>[A-Za-z_][A-Za-z0-9_]*))?:",
            RegexOptions.Compiled);

        readonly ITextBuffer buffer;
        readonly string filePath;

        public HppQuickInfoSource(ITextBuffer buffer, string filePath)
        {
            this.buffer = buffer;
            this.filePath = filePath;
        }

        public Task<QuickInfoItem> GetQuickInfoItemAsync(IAsyncQuickInfoSession session, CancellationToken cancellationToken)
        {
            var triggerPoint = session.GetTriggerPoint(buffer.CurrentSnapshot);
            if (triggerPoint == null || !triggerPoint.HasValue)
            {
                return Task.FromResult<QuickInfoItem>(null);
            }

            var point = triggerPoint.Value;
            var line = point.GetContainingLine();
            string lineText = line.GetText();
            int offset = point.Position - line.Start.Position;

            if (!TryGetWordSpan(lineText, offset, out int start, out int length))
            {
                return Task.FromResult<QuickInfoItem>(null);
            }

            string word = lineText.Substring(start, length);
            var applicableSpan = buffer.CurrentSnapshot.CreateTrackingSpan(
                new Span(line.Start.Position + start, length), SpanTrackingMode.EdgeInclusive);

            string content = BuildContent(word, lineText);
            if (content == null)
            {
                return Task.FromResult<QuickInfoItem>(null);
            }

            return Task.FromResult(new QuickInfoItem(applicableSpan, content));
        }

        string BuildContent(string word, string lineText)
        {
            if (word.StartsWith("@"))
            {
                string name = word.Substring(1);
                return CommandDocs.TryGetValue(name, out var doc) ? doc : null;
            }

            string actorName = word;
            string poseName = null;
            int dot = word.IndexOf('.');
            if (dot > 0)
            {
                actorName = word.Substring(0, dot);
                poseName = word.Substring(dot + 1);
            }
            else
            {
                var match = DialoguePrefix.Match(lineText);
                if (!match.Success || !string.Equals(match.Groups["actor"].Value, word, StringComparison.Ordinal))
                {
                    return null; // 不是台词行开头的角色名，也不是 Actor.Pose 形式，不认识这个词
                }

                if (match.Groups["pose"].Success)
                {
                    poseName = match.Groups["pose"].Value;
                }
            }

            string directory = string.IsNullOrEmpty(filePath) ? null : Path.GetDirectoryName(filePath);
            AliasDocument aliases = HppAliasFileLocator.FindAliasDocument(directory, out _);
            if (aliases == null || !aliases.Actors.TryGetValue(actorName, out var actor))
            {
                return null;
            }

            if (poseName == null)
            {
                return $"{actorName} -> raw '{actor.Raw}'" + (actor.Display != null ? $" ({actor.Display})" : string.Empty);
            }

            if (actor.Poses.TryGetValue(poseName, out var rawPose))
            {
                return $"{actorName}.{poseName} -> actor '{actor.Raw}', pose '{rawPose}'";
            }

            return $"{actorName} -> raw '{actor.Raw}'; pose '{poseName}' is not defined for this actor.";
        }

        static bool TryGetWordSpan(string text, int offset, out int start, out int length)
        {
            start = 0;
            length = 0;
            if (offset < 0 || offset > text.Length)
            {
                return false;
            }

            bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_' || c == '.' || c == '@';

            int s = offset;
            while (s > 0 && IsWordChar(text[s - 1]))
            {
                s--;
            }

            int e = offset;
            while (e < text.Length && IsWordChar(text[e]))
            {
                e++;
            }

            if (e <= s)
            {
                return false;
            }

            start = s;
            length = e - s;
            return true;
        }

        public void Dispose()
        {
        }
    }
}
