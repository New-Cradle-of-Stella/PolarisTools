using Microsoft.VisualStudio.Imaging.Interop;
using Microsoft.VisualStudio.Language.Intellisense;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace PolarisTools.Event.Language
{
    /// <summary>一个不带子操作、不带预览的最简单 <see cref="ISuggestedAction"/> 包装：显示文字 + 一个
    /// 同步执行的委托。拼写修正和"创建别名"两类 Code Action 都只需要这么多。</summary>
    internal sealed class HppSuggestedAction : ISuggestedAction
    {
        readonly Action apply;

        public HppSuggestedAction(string displayText, Action apply)
        {
            DisplayText = displayText;
            this.apply = apply;
        }

        public string DisplayText { get; }

        public string IconAutomationText => null;

        public ImageMoniker IconMoniker => default;

        public string InputGestureText => null;

        public bool HasActionSets => false;

        public bool HasPreview => false;

        public Task<object> GetPreviewAsync(CancellationToken cancellationToken) => Task.FromResult<object>(null);

        public Task<IEnumerable<SuggestedActionSet>> GetActionSetsAsync(CancellationToken cancellationToken)
            => Task.FromResult<IEnumerable<SuggestedActionSet>>(Array.Empty<SuggestedActionSet>());

        public void Invoke(CancellationToken cancellationToken) => apply();

        public bool TryGetTelemetryId(out Guid telemetryId)
        {
            telemetryId = Guid.Empty;
            return false;
        }

        public void Dispose()
        {
        }
    }
}
