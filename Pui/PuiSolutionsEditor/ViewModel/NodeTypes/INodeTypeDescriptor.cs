using System.Collections.Generic;

namespace PolarisTools.Pui.PuiSolutions.ViewModel.NodeTypes
{
    public interface INodeTypeDescriptor
    {
        NodeType Type { get; }
        string Title { get; }
        IReadOnlyList<ConnectorViewModel> CreateInputs();
        IReadOnlyList<ConnectorViewModel> CreateOutputs(object param);
        object CreateContent();
    }

    /// <summary>
    /// 节点类型描述符基类，提供默认实现
    /// </summary>
    public abstract class NodeTypeDescriptorBase : INodeTypeDescriptor
    {
        public abstract NodeType Type { get; }
        public abstract string Title { get; }
        public virtual IReadOnlyList<ConnectorViewModel> CreateInputs() => new List<ConnectorViewModel>();
        public virtual IReadOnlyList<ConnectorViewModel> CreateOutputs(object param) => new List<ConnectorViewModel>();
        public virtual object CreateContent() => null;
    }
}
