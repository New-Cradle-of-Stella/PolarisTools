using System.Collections.Generic;
using PolarisTools.Pui.PuiVisualEditor;

namespace PolarisTools.Pui.PuiSolutions.ViewModel.NodeTypes
{
    /// <summary>反序列化失败/未知类型字符串时的兜底占位，不通过任何菜单对外暴露。</summary>
    public class NormalDescriptor : NodeTypeDescriptorBase
    {
        public override NodeType Type => NodeType.Normal;
        public override string Title => "?";
    }

    /// <summary>固定入口节点：全图仅一个，不可删除，代表状态机的起始点。</summary>
    public class EntryDescriptor : NodeTypeDescriptorBase
    {
        public override NodeType Type => NodeType.Entry;
        public override string Title => "入口";

        public override IReadOnlyList<ConnectorViewModel> CreateOutputs(object param) => new List<ConnectorViewModel>
        {
            new() { Title = "初始状态", IsOutput = true }
        };
    }

    /// <summary>固定出口节点：全图仅一个，不可删除，代表退出整个状态机。只有一个输入连接器
    /// "退出"，任意 PuiState 节点的输出都可以连过来（一个输入连接器允许被多条连线指向，见
    /// EditorViewModel.CanConnect）。</summary>
    public class ExitDescriptor : NodeTypeDescriptorBase
    {
        public override NodeType Type => NodeType.Exit;
        public override string Title => "出口";

        public override IReadOnlyList<ConnectorViewModel> CreateInputs() => new List<ConnectorViewModel>
        {
            new() { Title = "退出" }
        };
    }

    /// <summary>
    /// 绑定到某个具体 .pui 文件的状态节点。输出连接器不是手动加的，而是该文件 Window 的
    /// "状态连接点"列表（PuiStateTransition）一一对应生成——param 约定为
    /// IReadOnlyList&lt;PuiStateTransition&gt;，由 EditorViewModel.AddPuiStateNodeAt/RefreshPuiStateNode
    /// 读取 .pui 文件后传入。
    /// </summary>
    public class PuiStateDescriptor : NodeTypeDescriptorBase
    {
        public override NodeType Type => NodeType.PuiState;
        public override string Title => "PUI 状态";

        public override IReadOnlyList<ConnectorViewModel> CreateInputs() => new List<ConnectorViewModel>
        {
            new() { Title = "进入" }
        };

        public override IReadOnlyList<ConnectorViewModel> CreateOutputs(object param)
        {
            var outputs = new List<ConnectorViewModel>();
            if (param is IReadOnlyList<PuiStateTransition> transitions)
            {
                foreach (PuiStateTransition t in transitions)
                {
                    outputs.Add(new ConnectorViewModel
                    {
                        Title = t.DisplayLabel,
                        IsOutput = true,
                        SourceTransitionId = t.Id,
                    });
                }
            }
            return outputs;
        }
    }
}
