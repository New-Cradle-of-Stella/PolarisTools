using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;
using System.Windows;

namespace PolarisTools.Pui.PuiSolutions.ViewModel
{
    public partial class NodeViewModel : ObservableObject
    {
        public string Title { get; set; }
        public NodeType Type { get; set; }

        [ObservableProperty]
        private Point _location;

        public ObservableCollection<object> ItemCollection { get; set; } = new ObservableCollection<object>();

        public ObservableCollection<ConnectorViewModel> Input { get; set; } = new ObservableCollection<ConnectorViewModel>();
        public ObservableCollection<ConnectorViewModel> Output { get; set; } = new ObservableCollection<ConnectorViewModel>();

        // PuiState 节点专属：绑定的 .pui 文件（磁盘绝对路径，运行期用；持久化时转成相对于
        // .puisln 所在目录的相对路径，见 PuislnNode.PuiRelativePath）。
        public string PuiFilePath { get; set; }

        // PuiState 节点专属：对应 IPUI.Name，规则跟 PolarisPuiGenerator.ComputeClassName/
        // GenerateCSharp 的 name 变量一致——.pui 文件名去掉扩展名。
        public string PuiName { get; set; }
    }

    public enum NodeType
    {
        // 反序列化失败/未知类型字符串时的兜底占位，不通过任何菜单对外暴露。
        Normal,

        // 固定入口节点：全图仅一个，不可删除，代表状态机的起始点。
        Entry,

        // 绑定到某个具体 .pui 文件的状态节点；输出连接器由该文件 Window 的
        // StateTransitions 列表决定，见 PuiStateDescriptor。
        PuiState,

        // 固定出口节点：全图仅一个，不可删除，代表退出整个状态机（PUISolution.Stop()）。
        Exit,
    }
}
