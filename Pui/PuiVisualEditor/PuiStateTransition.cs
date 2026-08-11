using CommunityToolkit.Mvvm.ComponentModel;
using System;

namespace PolarisTools.Pui.PuiVisualEditor
{
    public enum PuiStateTriggerType
    {
        ButtonClick,
        Cancel,
        CustomEvent
    }

    /// <summary>
    /// Window 专属的一条"状态连接点"：只描述触发方式和阻塞/非阻塞，不含跳转目标——
    /// 目标由 .puisln 图里从这个连接点画出的连线决定（同一个 .pui 可能被多个状态机图复用，
    /// 连到不同目标）。运行时/生成器统一靠 <see cref="ResolveTriggerKey"/> 得到的字符串做
    /// Polaris.PUI.PUIRuntime.RaiseEvent(triggerKey) 的 key（该调用会被路由给当前拥有这个
    /// PUI 实例的 Polaris.PUI.PUISolution）。
    /// </summary>
    public partial class PuiStateTransition : ObservableObject
    {
        // 不参与业务语义，仅用于 .puisln 侧"刷新节点"时按 Id 把旧连线迁移到新的输出连接器上
        // （行的位置、标签都可能变化，但 Id 不变）。
        [ObservableProperty]
        private string _id = Guid.NewGuid().ToString("N");

        [ObservableProperty]
        private PuiStateTriggerType _triggerType = PuiStateTriggerType.ButtonClick;

        // TriggerType == ButtonClick 时：本窗口内某个 Button 元素的 Name。
        [ObservableProperty]
        private string _buttonName = "";

        // TriggerType == CustomEvent 时：业务代码在 .pui.cs 里手动调用 Fire 时使用的事件名。
        [ObservableProperty]
        private string _eventKey = "";

        [ObservableProperty]
        private bool _blocking = true;

        /// <summary>
        /// 运行时保留 key，代表"取消/ESC"触发；必须与 Polaris.PUI.PUISolution.CancelTriggerKey
        /// 字面一致（两边各自定义一份常量，靠这个约定对齐，避免运行时项目反向依赖生成器项目）。
        /// </summary>
        public const string CancelTriggerKey = "@Cancel";

        public string ResolveTriggerKey() => TriggerType switch
        {
            PuiStateTriggerType.ButtonClick => ButtonName,
            PuiStateTriggerType.Cancel => CancelTriggerKey,
            PuiStateTriggerType.CustomEvent => EventKey,
            _ => null,
        };

        public string DisplayLabel => TriggerType switch
        {
            PuiStateTriggerType.ButtonClick => string.IsNullOrEmpty(ButtonName) ? "(no button selected)" : $"{ButtonName} click",
            PuiStateTriggerType.Cancel => "Cancel / ESC",
            PuiStateTriggerType.CustomEvent => string.IsNullOrEmpty(EventKey) ? "(unnamed event)" : $"Custom event: {EventKey}",
            _ => "?",
        };
    }
}
