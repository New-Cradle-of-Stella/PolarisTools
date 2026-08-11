using System.Collections.Generic;

namespace PolarisTools.Pui.PuiSolutions.ViewModel.NodeTypes
{
    public static class NodeTypeFactory
    {
        private static readonly Dictionary<NodeType, INodeTypeDescriptor> _descriptors = new()
        {
            [NodeType.Normal] = new NormalDescriptor(),
            [NodeType.Entry] = new EntryDescriptor(),
            [NodeType.PuiState] = new PuiStateDescriptor(),
            [NodeType.Exit] = new ExitDescriptor(),
        };

        public static INodeTypeDescriptor Get(NodeType type)
            => _descriptors.TryGetValue(type, out var d) ? d : _descriptors[NodeType.Normal];
    }
}
