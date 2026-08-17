using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Adornments;
using Microsoft.VisualStudio.Utilities;
using Polaris.Pevt.Actors;
using Polaris.Pevt.Binding;
using Polaris.Pevt.Commands;

namespace PolarisTools.Event.Pevt.Editor
{
    [Export(typeof(IAsyncQuickInfoSourceProvider))]
    [Name("PevtQuickInfo")]
    [ContentType(PevtContentType.Name)]
    [Order]
    internal sealed class PevtQuickInfoSourceProvider : IAsyncQuickInfoSourceProvider
    {
        public IAsyncQuickInfoSource? TryCreateQuickInfoSource(ITextBuffer textBuffer) =>
            textBuffer.Properties.GetOrCreateSingletonProperty(() => new PevtQuickInfoSource(textBuffer));
    }

    /// <summary>
    /// 快速信息。
    ///
    /// 计划要求"展示签名、返回值、等待方式和能力要求"——这四项全部来自
    /// <see cref="CommandDescriptor"/>，也就是运行时选择处理器时用的同一份描述目录，而不是
    /// 编辑器侧另写的一张文档表。同名重载全部列出，因为 PEVT 的重载靠参数数量与完整类型唯一确定，
    /// 只显示第一个会误导作者。
    /// </summary>
    internal sealed class PevtQuickInfoSource : IAsyncQuickInfoSource
    {
        private readonly ITextBuffer _buffer;

        public PevtQuickInfoSource(ITextBuffer buffer) => _buffer = buffer;

        public Task<QuickInfoItem?> GetQuickInfoItemAsync(IAsyncQuickInfoSession session, CancellationToken cancellationToken)
        {
            SnapshotPoint? trigger = session.GetTriggerPoint(_buffer.CurrentSnapshot);
            if (trigger == null)
                return Task.FromResult<QuickInfoItem?>(null);

            ITextSnapshot snapshot = trigger.Value.Snapshot;

            try
            {
                PevtSemanticModel? model = PevtSemanticModel.Create(snapshot.GetText(), cancellationToken);
                PevtSymbolOccurrence? occurrence = model?.FindAt(trigger.Value.Position);
                if (model == null || occurrence == null)
                    return Task.FromResult<QuickInfoItem?>(null);

                string? content = Describe(model, occurrence);
                if (content == null)
                    return Task.FromResult<QuickInfoItem?>(null);

                var applicable = snapshot.CreateTrackingSpan(
                    new Span(occurrence.Span.Start, Math.Max(1, Math.Min(occurrence.Span.Length, snapshot.Length - occurrence.Span.Start))),
                    SpanTrackingMode.EdgeInclusive);

                var element = new ContainerElement(
                    ContainerElementStyle.Stacked,
                    ClassifiedTextElement.CreatePlainText(content));

                return Task.FromResult<QuickInfoItem?>(new QuickInfoItem(applicable, element));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                return Task.FromResult<QuickInfoItem?>(null);
            }
        }

        private string? Describe(PevtSemanticModel model, PevtSymbolOccurrence occurrence)
        {
            switch (occurrence.Kind)
            {
                case PevtSymbolKind.BuiltinCall:
                {
                    IReadOnlyList<CommandDescriptor> overloads = CommandDescriptorCatalog.Builtin.Find(occurrence.Name);
                    if (overloads.Count == 0)
                        return "@" + occurrence.Name + "：未登记的内置事件语句。";

                    var lines = new List<string>();
                    foreach (CommandDescriptor descriptor in overloads)
                    {
                        lines.Add(PevtSignatureText.Describe(descriptor));
                        lines.Add(PevtSignatureText.DescribeDetails(descriptor));
                        lines.Add(string.Empty);
                    }

                    return string.Join(Environment.NewLine, lines).TrimEnd();
                }

                case PevtSymbolKind.Variable:
                case PevtSymbolKind.Constant:
                {
                    PevtSymbolOccurrence? declaration = model.FindDeclaration(occurrence);
                    if (declaration == null)
                        return occurrence.Name + "：当前文件里没有找到声明。";

                    string kindText = declaration.Kind == PevtSymbolKind.Constant ? "const" : "var";
                    string typeText = declaration.DeclaredType.HasValue ? declaration.DeclaredType.Value.DisplayName() : "?";
                    return kindText + " " + declaration.Name + " : " + typeText;
                }

                case PevtSymbolKind.Handler:
                    return "handler " + occurrence.Name
                        + Environment.NewLine
                        + "异步操作句柄。只能用于 await、kill 与 status；不参与普通类型系统。";

                case PevtSymbolKind.Block:
                {
                    PevtSymbolOccurrence? declaration = model.FindDeclaration(occurrence);
                    return declaration == null
                        ? occurrence.Name + "：当前文件里没有定义这个事件块。"
                        : "block " + declaration.Name + Environment.NewLine + "自定义事件块，定义在本文件内。";
                }

                case PevtSymbolKind.Label:
                    return "#" + occurrence.Name + Environment.NewLine + "标签。goto 的目标在加载期就已绑定。";

                default:
                    return null;
            }
        }

        public void Dispose()
        {
        }
    }

    /// <summary>
    /// 当前缓冲区所属的项目目录。
    ///
    /// 人物补全要合并"当前项目里的 <c>.pactor</c>"，因此需要知道文档在磁盘上的位置。走
    /// <see cref="ITextDocumentFactoryService"/> 拿真实路径而不是问 DTE：未保存的新文件没有项目归属，
    /// 这时候返回 null 让补全退回只用内置目录，而不是抛异常。
    /// </summary>
    internal static class PevtProjectContext
    {
        /// <summary>由 MEF 在 <see cref="PevtDocumentFactoryAccessor"/> 组合时注入。</summary>
        internal static ITextDocumentFactoryService? DocumentFactory { get; set; }

        public static string? PathFor(ITextBuffer buffer)
        {
            ITextDocumentFactoryService? factory = DocumentFactory;
            if (factory == null || buffer == null)
                return null;

            return factory.TryGetTextDocument(buffer, out ITextDocument document) ? document.FilePath : null;
        }

        /// <summary>
        /// 文档所在的项目根目录：从文件位置往上找第一个含 <c>.csproj</c> 的目录。
        /// 找不到时退回文件自己的目录。
        /// </summary>
        public static string? DirectoryFor(ITextBuffer buffer)
        {
            string? path = PathFor(buffer);
            if (string.IsNullOrEmpty(path))
                return null;

            try
            {
                var directory = new System.IO.DirectoryInfo(System.IO.Path.GetDirectoryName(path)!);
                System.IO.DirectoryInfo? probe = directory;
                while (probe != null)
                {
                    if (probe.GetFiles("*.csproj").Length > 0)
                        return probe.FullName;
                    probe = probe.Parent;
                }

                return directory.FullName;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }

    /// <summary>
    /// 把 <see cref="ITextDocumentFactoryService"/> 交给 <see cref="PevtProjectContext"/> 的组合钩子。
    ///
    /// 挂在 <see cref="Microsoft.VisualStudio.Text.Editor.ITextViewCreationListener"/> 上而不是自己
    /// 到处 <c>[Import]</c>：需要它的是几个静态辅助方法（人物补全要知道项目目录），而 MEF 只会
    /// 向被组合出来的部件注入依赖。第一个 <c>.pevt</c> 视图打开时它就位了。
    /// </summary>
    [Export(typeof(Microsoft.VisualStudio.Text.Editor.ITextViewCreationListener))]
    [ContentType(PevtContentType.Name)]
    [Microsoft.VisualStudio.Text.Editor.TextViewRole(Microsoft.VisualStudio.Text.Editor.PredefinedTextViewRoles.Document)]
    internal sealed class PevtDocumentFactoryAccessor : Microsoft.VisualStudio.Text.Editor.ITextViewCreationListener
    {
        [Import]
        internal ITextDocumentFactoryService DocumentFactory { get; set; } = null!;

        public void TextViewCreated(Microsoft.VisualStudio.Text.Editor.ITextView textView) =>
            PevtProjectContext.DocumentFactory = DocumentFactory;
    }
}
