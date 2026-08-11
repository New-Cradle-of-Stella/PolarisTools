using CommunityToolkit.Mvvm.ComponentModel;
using System.Windows;

namespace PolarisTools.Pui.PuiSolutions.ViewModel
{
    public partial class ConnectorViewModel : ObservableObject
    {
        [ObservableProperty]
        private Point _anchor;

        [ObservableProperty]
        private bool _isConnected;

        public string Title { get; set; }

        public bool IsOutput { get; set; }

        // 仅输出连接器使用：对应 PuiStateTransition.Id，供"刷新节点"时把旧连线
        // 按 Id 重新匹配到刷新后的新连接器上（见 EditorViewModel.RefreshPuiStateNode）。
        public string SourceTransitionId { get; set; }
    }
}
