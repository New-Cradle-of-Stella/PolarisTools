using CommunityToolkit.Mvvm.Input;
using PolarisTools.Pui.PuiSolutions.ViewModel.NodeTypes;
using PolarisTools.Pui.PuiVisualEditor;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;

namespace PolarisTools.Pui.PuiSolutions.ViewModel
{
    public class EditorViewModel
    {
        public ObservableCollection<NodeViewModel> Nodes { get; } = new();
        public ObservableCollection<ConnectionViewModel> Connections { get; } = new();
        public ObservableCollection<object> SelectedConnections { get; } = new();
        public PendingConnectionViewModel PendingConnection { get; }
        public ICommand RemoveConnectionCommand { get; }

        public string FilePath { get; set; }
        private bool _isDirty;
        private bool _suspendDirty;

        public bool IsDirty
        {
            get => _isDirty;
            private set
            {
                if (_isDirty == value) return;
                _isDirty = value;
                IsDirtyChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public event EventHandler IsDirtyChanged;

        public EditorViewModel()
        {
            PendingConnection = new PendingConnectionViewModel(this);
            RemoveConnectionCommand = new RelayCommand<object>(
                param => RemoveConnection(param as ConnectionViewModel),
                param => param is ConnectionViewModel c && c.Removable);

            Nodes.CollectionChanged += OnNodesCollectionChanged;
            Connections.CollectionChanged += (_, _) => MarkDirty();

            _suspendDirty = true;
            try { EnsureEntryNode(); EnsureExitNode(); }
            finally { _suspendDirty = false; }
        }

        public void ClearGraph()
        {
            _suspendDirty = true;
            try
            {
                Connections.Clear();
                SelectedConnections.Clear();
                Nodes.Clear();
                FilePath = null;
                EnsureEntryNode();
                EnsureExitNode();
            }
            finally { _suspendDirty = false; }
            IsDirty = true;
        }

        /// <summary>保证图里始终恰好一个不可删除的入口节点；已存在则什么都不做。</summary>
        private void EnsureEntryNode()
        {
            if (Nodes.Any(n => n.Type == NodeType.Entry)) return;
            AddNodeAt(NodeType.Entry, new Point(40, 40));
        }

        /// <summary>保证图里始终恰好一个不可删除的出口节点；已存在则什么都不做。</summary>
        private void EnsureExitNode()
        {
            if (Nodes.Any(n => n.Type == NodeType.Exit)) return;
            AddNodeAt(NodeType.Exit, new Point(240, 40));
        }

        public NodeViewModel AddNodeAt(NodeType type, Point location, object param = null)
        {
            var node = CreateNode(type, param);
            node.Location = location;
            MarkDirty();
            return node;
        }

        /// <summary>右键菜单选中同目录下某个 .pui 文件时调用：读取该文件 Window 的状态连接点
        /// 列表，创建一个绑定到它的 PuiState 节点。</summary>
        public NodeViewModel AddPuiStateNodeAt(string puiFilePath, Point location)
        {
            IReadOnlyList<PuiStateTransition> transitions = LoadStateTransitions(puiFilePath);
            NodeViewModel node = AddNodeAt(NodeType.PuiState, location, transitions);
            node.Title = Path.GetFileNameWithoutExtension(puiFilePath);
            node.PuiFilePath = puiFilePath;
            node.PuiName = node.Title;
            return node;
        }

        /// <summary>重新读取节点绑定的 .pui 文件，按当前状态连接点列表重建输出连接器；
        /// 未变的连接点（按 Id 匹配）连线保留，匹配不到的连线被强制断开。</summary>
        public void RefreshPuiStateNode(NodeViewModel node)
        {
            if (node == null || node.Type != NodeType.PuiState || string.IsNullOrEmpty(node.PuiFilePath))
                return;

            IReadOnlyList<PuiStateTransition> transitions = LoadStateTransitions(node.PuiFilePath);
            var newOutputs = new List<ConnectorViewModel>(NodeTypeFactory.Get(NodeType.PuiState).CreateOutputs(transitions));

            foreach (ConnectorViewModel oldConn in node.Output.ToList())
            {
                ConnectorViewModel match = newOutputs.FirstOrDefault(c => c.SourceTransitionId == oldConn.SourceTransitionId);
                foreach (ConnectionViewModel conn in Connections.Where(c => c.Source == oldConn).ToList())
                {
                    ConnectorViewModel target = conn.Target;
                    bool removable = conn.Removable;
                    Connections.Remove(conn);
                    SelectedConnections.Remove(conn);
                    if (match != null)
                        Connections.Add(new ConnectionViewModel(match, target, removable));
                    else
                        RefreshConnectorConnectedState(target);
                }
            }

            node.Output.Clear();
            foreach (ConnectorViewModel c in newOutputs)
                node.Output.Add(c);

            MarkDirty();
        }

        /// <summary>删除一个非入口/出口节点，连带断开它牵涉的所有连线。入口/出口节点不可删除，直接忽略。</summary>
        public void DeleteNode(NodeViewModel node)
        {
            if (node == null || node.Type == NodeType.Entry || node.Type == NodeType.Exit) return;

            foreach (ConnectorViewModel c in node.Input.Concat(node.Output).ToList())
                ForceRemoveConnectionsTouching(c);

            Nodes.Remove(node);
            MarkDirty();
        }

        // net472 没有 Path.GetRelativePath（.NET Core 2.0+ 才有），用 Uri 手算等价效果。
        private static string MakeRelativePath(string baseDir, string targetPath)
        {
            string baseWithSlash = baseDir.EndsWith(Path.DirectorySeparatorChar.ToString())
                ? baseDir : baseDir + Path.DirectorySeparatorChar;
            var baseUri = new Uri(baseWithSlash);
            var targetUri = new Uri(targetPath);
            string relative = Uri.UnescapeDataString(baseUri.MakeRelativeUri(targetUri).ToString());
            return relative.Replace('/', Path.DirectorySeparatorChar);
        }

        private static IReadOnlyList<PuiStateTransition> LoadStateTransitions(string puiFilePath)
        {
            if (string.IsNullOrEmpty(puiFilePath) || !File.Exists(puiFilePath))
                return Array.Empty<PuiStateTransition>();

            string xml = File.ReadAllText(puiFilePath);
            PuiElement root = PolarisPuiGenerator.ParseRoot(xml, Path.GetFileNameWithoutExtension(puiFilePath));
            return root.StateTransitions.ToList();
        }

        public void MarkDirty()
        {
            if (_suspendDirty) return;
            IsDirty = true;
        }

        public void ClearDirty() => IsDirty = false;

        private void OnNodesCollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            if (e.NewItems != null)
            {
                foreach (NodeViewModel n in e.NewItems)
                    HookNode(n);
            }
            MarkDirty();
        }

        private void HookNode(NodeViewModel n)
        {
            if (n == null) return;
            n.PropertyChanged -= OnNodePropertyChanged;
            n.PropertyChanged += OnNodePropertyChanged;
            n.ItemCollection.CollectionChanged -= OnNodeItemsChanged;
            n.ItemCollection.CollectionChanged += OnNodeItemsChanged;
            n.Input.CollectionChanged -= OnNodeConnectorsChanged;
            n.Input.CollectionChanged += OnNodeConnectorsChanged;
            n.Output.CollectionChanged -= OnNodeConnectorsChanged;
            n.Output.CollectionChanged += OnNodeConnectorsChanged;
        }

        private void OnNodePropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(NodeViewModel.Location)
                || e.PropertyName == nameof(NodeViewModel.Title)
                || e.PropertyName == nameof(NodeViewModel.Type))
                MarkDirty();
        }

        private void OnNodeItemsChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
            => MarkDirty();

        private void OnNodeConnectorsChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
            => MarkDirty();

        public void SaveToFile(string path)
        {
            string puislnDir = Path.GetDirectoryName(path);
            var doc = new PuislnDocument();
            var connectorIndex = new Dictionary<ConnectorViewModel, (int node, bool isOut, int idx)>();
            for (int i = 0; i < Nodes.Count; i++)
            {
                var n = Nodes[i];
                var pn = new PuislnNode
                {
                    Title = n.Title,
                    Type = n.Type.ToString(),
                    X = n.Location.X,
                    Y = n.Location.Y,
                    ItemCollection = n.ItemCollection.Select(x => x?.ToString() ?? "").ToList(),
                    PuiRelativePath = !string.IsNullOrEmpty(n.PuiFilePath) && !string.IsNullOrEmpty(puislnDir)
                        ? MakeRelativePath(puislnDir, n.PuiFilePath)
                        : null,
                    PuiName = n.PuiName,
                };
                for (int k = 0; k < n.Input.Count; k++)
                {
                    var c = n.Input[k];
                    connectorIndex[c] = (i, false, k);
                    pn.Inputs.Add(new PuislnConnector
                    {
                        Title = c.Title,
                        IsOutput = false,
                        SourceTransitionId = c.SourceTransitionId,
                    });
                }
                for (int k = 0; k < n.Output.Count; k++)
                {
                    var c = n.Output[k];
                    connectorIndex[c] = (i, true, k);
                    pn.Outputs.Add(new PuislnConnector
                    {
                        Title = c.Title,
                        IsOutput = true,
                        SourceTransitionId = c.SourceTransitionId,
                    });
                }
                doc.Nodes.Add(pn);
            }
            foreach (var conn in Connections)
            {
                if (!connectorIndex.TryGetValue(conn.Source, out var s)) continue;
                if (!connectorIndex.TryGetValue(conn.Target, out var t)) continue;
                doc.Connections.Add(new PuislnConnection
                {
                    SourceNode = s.node,
                    SourceIsOutput = s.isOut,
                    SourceIndex = s.idx,
                    TargetNode = t.node,
                    TargetIsOutput = t.isOut,
                    TargetIndex = t.idx,
                    Removable = conn.Removable
                });
            }
            PuislnSerializer.Save(path, doc);
            FilePath = path;
            ClearDirty();
        }

        public void LoadFromFile(string path)
        {
            var doc = PuislnSerializer.Load(path);
            LoadFromDocument(doc, Path.GetDirectoryName(path));
            FilePath = path;
            ClearDirty();
        }

        public void LoadFromDocument(PuislnDocument doc, string basePath = null)
        {
            _suspendDirty = true;
            try
            {
                Connections.Clear();
                SelectedConnections.Clear();
                Nodes.Clear();
                foreach (var pn in doc.Nodes)
                {
                    var node = new NodeViewModel
                    {
                        Title = pn.Title ?? "",
                        Type = Enum.TryParse<NodeType>(pn.Type, out var nt) ? nt : NodeType.Normal,
                        Location = new Point(pn.X, pn.Y),
                        PuiName = pn.PuiName,
                        PuiFilePath = !string.IsNullOrEmpty(pn.PuiRelativePath) && !string.IsNullOrEmpty(basePath)
                            ? Path.GetFullPath(Path.Combine(basePath, pn.PuiRelativePath))
                            : null,
                    };
                    if (pn.ItemCollection != null)
                    {
                        foreach (var item in pn.ItemCollection)
                            node.ItemCollection.Add(item);
                    }
                    if (pn.Inputs != null)
                    {
                        foreach (var c in pn.Inputs)
                            node.Input.Add(FromDto(c, isOutput: false));
                    }
                    if (pn.Outputs != null)
                    {
                        foreach (var c in pn.Outputs)
                            node.Output.Add(FromDto(c, isOutput: true));
                    }
                    Nodes.Add(node);
                }
                foreach (var pc in doc.Connections)
                {
                    if (pc.SourceNode < 0 || pc.SourceNode >= Nodes.Count) continue;
                    if (pc.TargetNode < 0 || pc.TargetNode >= Nodes.Count) continue;
                    var srcNode = Nodes[pc.SourceNode];
                    var dstNode = Nodes[pc.TargetNode];
                    var srcList = pc.SourceIsOutput ? srcNode.Output : srcNode.Input;
                    var dstList = pc.TargetIsOutput ? dstNode.Output : dstNode.Input;
                    if (pc.SourceIndex < 0 || pc.SourceIndex >= srcList.Count) continue;
                    if (pc.TargetIndex < 0 || pc.TargetIndex >= dstList.Count) continue;
                    Connections.Add(new ConnectionViewModel(
                        srcList[pc.SourceIndex],
                        dstList[pc.TargetIndex],
                        removable: pc.Removable));
                }
                EnsureEntryNode();
                EnsureExitNode();
            }
            finally
            {
                _suspendDirty = false;
            }
        }

        private static ConnectorViewModel FromDto(PuislnConnector c, bool isOutput)
        {
            return new ConnectorViewModel
            {
                Title = c.Title ?? "",
                SourceTransitionId = c.SourceTransitionId,
                IsOutput = isOutput || c.IsOutput
            };
        }

        public bool TryConnect(ConnectorViewModel a, ConnectorViewModel b)
        {
            if (!TryNormalizeDirection(a, b, out var from, out var to))
                return false;
            if (!CanConnect(from, to))
                return false;
            Connections.Add(new ConnectionViewModel(from, to, removable: true));
            return true;
        }

        private static bool TryNormalizeDirection(
            ConnectorViewModel a, ConnectorViewModel b,
            out ConnectorViewModel from, out ConnectorViewModel to)
        {
            from = to = null!;
            if (a == null || b == null) return false;
            if (a.IsOutput && !b.IsOutput)
            {
                from = a; to = b;
                return true;
            }
            if (b.IsOutput && !a.IsOutput)
            {
                from = b; to = a;
                return true;
            }
            return false;
        }

        public bool CanConnect(ConnectorViewModel from, ConnectorViewModel to)
        {
            if (from == null || to == null)
                return false;
            if (ReferenceEquals(from, to))
                return false;
            if (!from.IsOutput || to.IsOutput)
                return false;
            // 一个输出连接器最多只能有一条出边（同一个 (来源节点, 触发键) 只能指向一个目标，
            // 见 PolarisPuislnGenerator 里的重复触发键检查）；输入连接器则允许被多条连线指向。
            if (Connections.Any(c => c.Source == from || c.Target == from))
                return false;
            return true;
        }

        public void DeleteSelectedConnections()
        {
            var toRemove = SelectedConnections
                .OfType<ConnectionViewModel>()
                .Where(c => c.Removable)
                .ToList();

            foreach (var c in toRemove)
                RemoveConnection(c);
        }

        private void RemoveConnection(ConnectionViewModel connection)
        {
            if (connection == null || !connection.Removable || !Connections.Contains(connection))
                return;

            Connections.Remove(connection);
            SelectedConnections.Remove(connection);
            RefreshConnectorConnectedState(connection.Source);
            RefreshConnectorConnectedState(connection.Target);
        }

        private void RefreshConnectorConnectedState(ConnectorViewModel connector)
        {
            if (connector == null) return;
            connector.IsConnected = Connections.Any(c => c.Source == connector || c.Target == connector);
        }

        private NodeViewModel CreateNode(NodeType type, object param = null!)
        {
            var descriptor = NodeTypeFactory.Get(type);
            var node = new NodeViewModel
            {
                Type = type,
                Title = descriptor.Title,
                Input = new ObservableCollection<ConnectorViewModel>(descriptor.CreateInputs()),
                Output = new ObservableCollection<ConnectorViewModel>(descriptor.CreateOutputs(param))
            };

            Nodes.Add(node);
            return node;
        }

        /// <summary>无条件断开牵涉该连接器的所有连线，忽略 Removable——用于节点删除/刷新场景：
        /// 连接器本身即将消失，Removable 只用来保护"用户手动删连线"，不该阻止这里的强制清理。</summary>
        public void ForceRemoveConnectionsTouching(ConnectorViewModel connector)
        {
            var list = Connections
                .Where(c => c.Source == connector || c.Target == connector)
                .ToList();
            foreach (var c in list)
            {
                Connections.Remove(c);
                SelectedConnections.Remove(c);
                RefreshConnectorConnectedState(c.Source);
                RefreshConnectorConnectedState(c.Target);
            }
        }
    }
}
