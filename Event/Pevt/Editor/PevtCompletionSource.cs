using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.ComponentModel.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion.Data;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Adornments;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Utilities;
using Polaris.Pevt.Actors;
using Polaris.Pevt.Binding;
using Polaris.Pevt.Commands;
using Polaris.Pevt.Syntax;

namespace PolarisTools.Event.Pevt.Editor
{
    [Export(typeof(IAsyncCompletionSourceProvider))]
    [Name("PevtCompletion")]
    [ContentType(PevtContentType.Name)]
    internal sealed class PevtCompletionSourceProvider : IAsyncCompletionSourceProvider
    {
        public IAsyncCompletionSource GetOrCreate(ITextView textView) =>
            textView.Properties.GetOrCreateSingletonProperty(() => new PevtCompletionSource());
    }

    /// <summary>
    /// 补全。
    ///
    /// 建在 <see cref="PevtSemanticModel"/> 之上，因此候选项永远来自共享 Core 的事实：
    /// 关键字来自 <see cref="SyntaxFacts.Keywords"/>，<c>@</c> 名称与重载来自
    /// <see cref="CommandDescriptorCatalog.Builtin"/>，参数域来自各形参上登记的
    /// <see cref="Polaris.Pevt.Binding.ParameterDomain"/>，人物来自内置目录与当前项目 <c>.pactor</c> 的合并。
    /// 编辑器侧不维护第二份名单，也就不存在"补全里有、运行时没有"这种偏移。
    ///
    /// 触发上下文按光标左侧的字面形状判断，而不是等语法树成型：编辑中的文档大量处于
    /// <c>@say(</c> 这种不完整状态，而那正是最需要补全的时刻。
    /// </summary>
    internal sealed class PevtCompletionSource : IAsyncCompletionSource
    {
        /// <summary>参数域名 → 该域在补全里提供哪一类候选。</summary>
        private enum DomainCategory
        {
            None,
            Actor,
            Appearance,
            Anchor,
            ClosedSet,
        }

        public CompletionStartData InitializeCompletion(CompletionTrigger trigger, SnapshotPoint triggerLocation, CancellationToken token)
        {
            ITextSnapshot snapshot = triggerLocation.Snapshot;
            int position = triggerLocation.Position;

            // 可替换范围：光标左侧连续的标识符字符。`@`、`_`、`:`、`-` 都算进去，
            // 因为 `@actor_enter`、`_helper` 和 `aic:noel` 是一个整体，拆开补全会很别扭。
            int start = position;
            while (start > 0 && IsWordChar(snapshot[start - 1]))
                start--;

            return new CompletionStartData(CompletionParticipation.ProvidesItems, new SnapshotSpan(snapshot, start, position - start));
        }

        private static bool IsWordChar(char c) =>
            char.IsLetterOrDigit(c) || c == '_' || c == '@' || c == ':' || c == '-' || c == '.';

        public Task<CompletionContext> GetCompletionContextAsync(
            IAsyncCompletionSession session,
            CompletionTrigger trigger,
            SnapshotPoint triggerLocation,
            SnapshotSpan applicableToSpan,
            CancellationToken token)
        {
            string text = triggerLocation.Snapshot.GetText();
            string? projectDirectory = PevtProjectContext.DirectoryFor(triggerLocation.Snapshot.TextBuffer);

            var items = new List<CompletionItem>();

            try
            {
                Build(items, text, triggerLocation.Position, projectDirectory, token);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                // 补全出错就少给几项，绝不让编辑器弹异常。
            }

            return Task.FromResult(new CompletionContext(items.ToImmutableArray()));
        }

        private void Build(List<CompletionItem> items, string text, int position, string? projectDirectory, CancellationToken token)
        {
            PevtSemanticModel? model = PevtSemanticModel.Create(text, token);
            if (model == null)
                return;

            PevtCompletionContextKind.Kind kind = PevtCompletionContextKind.Detect(text, position, out string? callName, out int argumentIndex);

            switch (kind)
            {
                case PevtCompletionContextKind.Kind.BuiltinName:
                    AddBuiltinNames(items, model);
                    return;

                case PevtCompletionContextKind.Kind.BlockName:
                    AddBlockNames(items, model);
                    return;

                case PevtCompletionContextKind.Kind.LabelName:
                    AddLabels(items, model);
                    return;

                case PevtCompletionContextKind.Kind.Argument:
                    AddArgumentCandidates(items, model, callName!, argumentIndex, projectDirectory);
                    // 实参位置同样可以引用变量与常量。
                    AddVisibleNames(items, model, position);
                    return;

                default:
                    AddKeywords(items);
                    AddVisibleNames(items, model, position);
                    AddBlockNames(items, model);
                    return;
            }
        }

        private void AddKeywords(List<CompletionItem> items)
        {
            foreach (string keyword in SyntaxFacts.Keywords.Keys.OrderBy(k => k, StringComparer.Ordinal))
                items.Add(new CompletionItem(keyword, this, Glyphs.Keyword));
        }

        private void AddBuiltinNames(List<CompletionItem> items, PevtSemanticModel model)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (CommandDescriptor descriptor in CommandDescriptorCatalog.Builtin.Descriptors)
            {
                // `_start` 变体只有文件声明了 enable async 才能调用，没声明时不该出现在补全里。
                if (descriptor.IsAsync && !model.HasAsyncCapability)
                    continue;
                if (!seen.Add(descriptor.Name))
                    continue;

                items.Add(new CompletionItem(descriptor.Name, this, Glyphs.Method));
            }
        }

        private void AddBlockNames(List<CompletionItem> items, PevtSemanticModel model)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (PevtSymbolOccurrence occurrence in model.Occurrences)
            {
                if (occurrence.Kind == PevtSymbolKind.Block && occurrence.IsDeclaration && seen.Add(occurrence.Name))
                    items.Add(new CompletionItem(occurrence.Name, this, Glyphs.Method));
            }
        }

        private void AddLabels(List<CompletionItem> items, PevtSemanticModel model)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (PevtSymbolOccurrence occurrence in model.Occurrences)
            {
                if (occurrence.Kind == PevtSymbolKind.Label && occurrence.IsDeclaration && seen.Add(occurrence.Name))
                    items.Add(new CompletionItem(occurrence.Name, this, Glyphs.Label));
            }
        }

        private void AddVisibleNames(List<CompletionItem> items, PevtSemanticModel model, int position)
        {
            foreach (PevtSymbolOccurrence occurrence in model.VisibleDeclarationsBefore(position))
            {
                if (occurrence.Kind == PevtSymbolKind.Block)
                    continue;

                ImageElement glyph = occurrence.Kind == PevtSymbolKind.Handler ? Glyphs.Handler : Glyphs.Variable;
                string suffix = occurrence.DeclaredType.HasValue ? occurrence.DeclaredType.Value.DisplayName() : occurrence.Kind.ToString();
                items.Add(new CompletionItem(occurrence.Name, this, glyph, ImmutableArray<CompletionFilter>.Empty, suffix,
                    occurrence.Name, occurrence.Name, occurrence.Name, ImmutableArray<ImageElement>.Empty));
            }
        }

        /// <summary>
        /// 实参位置的候选。
        ///
        /// 先按名称拿到该 <c>@</c> 的全部重载，再看第 <paramref name="argumentIndex"/> 个形参上登记的
        /// 参数域：封闭集直接给取值（<c>easing</c> 的四个值），人物域给合并后的人物 ID，
        /// appearance/anchor 域尝试用同一次调用里已经写好的 actorId 收窄到那个人物自己的登记项。
        /// </summary>
        private void AddArgumentCandidates(List<CompletionItem> items, PevtSemanticModel model, string callName, int argumentIndex, string? projectDirectory)
        {
            IReadOnlyList<CommandDescriptor> overloads = CommandDescriptorCatalog.Builtin.Find(callName);
            if (overloads.Count == 0)
                return;

            var closed = new HashSet<string>(StringComparer.Ordinal);
            DomainCategory category = DomainCategory.None;

            foreach (CommandDescriptor descriptor in overloads)
            {
                if (argumentIndex < 0 || argumentIndex >= descriptor.Parameters.Count)
                    continue;

                ParameterDomain? domain = descriptor.Parameters[argumentIndex].Domain;
                if (domain == null)
                    continue;

                if (domain.ClosedValues != null && domain.ClosedValues.Count > 0)
                {
                    category = DomainCategory.ClosedSet;
                    foreach (string value in domain.ClosedValues)
                        closed.Add(value);
                    continue;
                }

                if (domain.Name == "actor-id")
                    category = DomainCategory.Actor;
                else if (domain.Name == "actor-appearance" || domain.Name == "actor-portrait" || domain.Name == "actor-ui-portrait")
                    category = DomainCategory.Appearance;
                else if (domain.Name == "actor-anchor")
                    category = DomainCategory.Anchor;
            }

            switch (category)
            {
                case DomainCategory.ClosedSet:
                    foreach (string value in closed.OrderBy(v => v, StringComparer.Ordinal))
                        items.Add(new CompletionItem("\"" + value + "\"", this, Glyphs.Constant));
                    return;

                case DomainCategory.Actor:
                    foreach (string actorId in PevtActorIndex.ActorIds(projectDirectory))
                        items.Add(new CompletionItem("\"" + actorId + "\"", this, Glyphs.Constant));
                    return;

                case DomainCategory.Appearance:
                case DomainCategory.Anchor:
                    AddActorScopedIds(items, category, projectDirectory);
                    return;
            }
        }

        /// <summary>
        /// appearance / anchor 的候选。
        ///
        /// 收窄到"哪个人物"需要读同一次调用里前面那个 actorId 实参，而编辑中的调用往往还不成节点；
        /// 因此这里给出全部可见人物的登记项并加人物 ID 作后缀，让作者自己挑——宁可多给几项，
        /// 也不要因为解析不出人物就一项都不给。
        /// </summary>
        private void AddActorScopedIds(List<CompletionItem> items, DomainCategory category, string? projectDirectory)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (ActorCatalog catalog in PevtActorIndex.Catalogs(projectDirectory))
            {
                foreach (ActorDefinition actor in catalog.Actors)
                {
                    string actorId = catalog.GetActorId(actor);

                    IEnumerable<string> ids = category == DomainCategory.Anchor
                        ? actor.Anchors.Select(a => a.Id)
                        : actor.Appearances.Select(a => a.Id).Concat(actor.Portraits.Select(p => p.Id));

                    foreach (string id in ids)
                    {
                        if (string.IsNullOrEmpty(id) || !seen.Add(actorId + "/" + id))
                            continue;

                        items.Add(new CompletionItem("\"" + id + "\"", this, Glyphs.Constant,
                            ImmutableArray<CompletionFilter>.Empty, actorId, "\"" + id + "\"", id, id,
                            ImmutableArray<ImageElement>.Empty));
                    }
                }
            }

            if (category == DomainCategory.Anchor)
            {
                foreach (string anchor in BuiltinActorAnchors.All.OrderBy(a => a, StringComparer.Ordinal))
                {
                    if (seen.Add("<builtin>/" + anchor))
                        items.Add(new CompletionItem("\"" + anchor + "\"", this, Glyphs.Constant,
                            ImmutableArray<CompletionFilter>.Empty, "语义锚点", "\"" + anchor + "\"", anchor, anchor,
                            ImmutableArray<ImageElement>.Empty));
                }
            }
        }

        /// <summary>补全项的说明面板。<c>@</c> 名称给出全部重载的签名与执行方式。</summary>
        public Task<object?> GetDescriptionAsync(IAsyncCompletionSession session, CompletionItem item, CancellationToken token)
        {
            IReadOnlyList<CommandDescriptor> overloads = CommandDescriptorCatalog.Builtin.Find(item.DisplayText);
            if (overloads.Count == 0)
                return Task.FromResult<object?>(null);

            var lines = new List<string>();
            foreach (CommandDescriptor descriptor in overloads)
            {
                lines.Add(PevtSignatureText.Describe(descriptor));
                lines.Add(PevtSignatureText.DescribeDetails(descriptor));
                lines.Add(string.Empty);
            }

            return Task.FromResult<object?>(string.Join(Environment.NewLine, lines).TrimEnd());
        }

        /// <summary>补全项图标。用 VS 自带的标准图元，跟随主题。</summary>
        private static class Glyphs
        {
            public static readonly ImageElement Keyword = new ImageElement(new Microsoft.VisualStudio.Core.Imaging.ImageId(
                new Guid("ae27a6b0-e345-4288-96df-5eaf394ee369"), 1589)); // KnownMonikers.IntellisenseKeyword

            public static readonly ImageElement Method = new ImageElement(new Microsoft.VisualStudio.Core.Imaging.ImageId(
                new Guid("ae27a6b0-e345-4288-96df-5eaf394ee369"), 1874)); // KnownMonikers.Method

            public static readonly ImageElement Variable = new ImageElement(new Microsoft.VisualStudio.Core.Imaging.ImageId(
                new Guid("ae27a6b0-e345-4288-96df-5eaf394ee369"), 2851)); // KnownMonikers.LocalVariable

            public static readonly ImageElement Constant = new ImageElement(new Microsoft.VisualStudio.Core.Imaging.ImageId(
                new Guid("ae27a6b0-e345-4288-96df-5eaf394ee369"), 616)); // KnownMonikers.Constant

            public static readonly ImageElement Handler = new ImageElement(new Microsoft.VisualStudio.Core.Imaging.ImageId(
                new Guid("ae27a6b0-e345-4288-96df-5eaf394ee369"), 1041)); // KnownMonikers.Event

            public static readonly ImageElement Label = new ImageElement(new Microsoft.VisualStudio.Core.Imaging.ImageId(
                new Guid("ae27a6b0-e345-4288-96df-5eaf394ee369"), 1727)); // KnownMonikers.Label
        }
    }

    /// <summary>
    /// 光标所处的补全上下文。
    ///
    /// 按字面形状而不是语法树判断，理由见 <see cref="PevtCompletionSource"/>：补全最需要工作的时刻
    /// 恰好是文档语法不完整的时刻。规则只有四条，都只看光标左侧那一小段。
    /// </summary>
    internal static class PevtCompletionContextKind
    {
        internal enum Kind
        {
            Statement,
            BuiltinName,
            BlockName,
            LabelName,
            Argument,
        }

        public static Kind Detect(string text, int position, out string? callName, out int argumentIndex)
        {
            callName = null;
            argumentIndex = -1;

            if (position > text.Length)
                position = text.Length;

            int lineStart = text.LastIndexOf('\n', Math.Max(0, position - 1)) + 1;
            string line = text.Substring(lineStart, position - lineStart);

            // 光标正在写一个标识符时，往左跳过它，再看引导字符。
            int cursor = line.Length;
            while (cursor > 0 && (char.IsLetterOrDigit(line[cursor - 1]) || line[cursor - 1] == '_'))
                cursor--;

            if (cursor > 0)
            {
                char lead = line[cursor - 1];
                if (lead == '@')
                    return Kind.BuiltinName;
                if (lead == '#')
                    return Kind.LabelName;
            }

            // 括号内 → 实参位置。从光标往左找未闭合的 `(`，同时数逗号。
            int depth = 0;
            int commas = 0;
            for (int i = line.Length - 1; i >= 0; i--)
            {
                char c = line[i];
                if (c == ')')
                {
                    depth++;
                }
                else if (c == '(')
                {
                    if (depth == 0)
                    {
                        callName = ReadCallNameBefore(line, i);
                        if (callName == null)
                            return Kind.Statement;

                        argumentIndex = commas;
                        return Kind.Argument;
                    }

                    depth--;
                }
                else if (c == ',' && depth == 0)
                {
                    commas++;
                }
            }

            // 行首那个 `_` 是自定义事件块调用。
            string trimmed = line.TrimStart();
            if (trimmed.StartsWith("_", StringComparison.Ordinal) && !trimmed.Contains("("))
                return Kind.BlockName;

            return Kind.Statement;
        }

        /// <summary>读 <c>(</c> 左侧的调用名；不是 <c>@name(</c> 形状时返回 null。</summary>
        private static string? ReadCallNameBefore(string line, int parenIndex)
        {
            int end = parenIndex;
            int start = end;
            while (start > 0 && (char.IsLetterOrDigit(line[start - 1]) || line[start - 1] == '_'))
                start--;

            if (start == end || start == 0 || line[start - 1] != '@')
                return null;

            return line.Substring(start, end - start);
        }
    }
}
