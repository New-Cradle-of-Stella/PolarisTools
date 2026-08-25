using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Polaris.AI.Authoring;

namespace PolarisTools.AI.BehaviorEditor;

public partial class PaiEditorControl : UserControl
{
    readonly PaiEditorViewModel viewModel = new PaiEditorViewModel();
    string? path;

    public PaiEditorControl()
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.DirtyChanged += (_, __) => DirtyChanged?.Invoke(this, EventArgs.Empty);
        viewModel.SelectionChanged += (_, __) => RefreshJsonEditors();
    }

    public bool IsDirty => viewModel.IsDirty;
    public event EventHandler? DirtyChanged;

    public void LoadFile(string filePath)
    {
        path = filePath;
        PaiDocument document;
        if (!File.Exists(filePath) || new FileInfo(filePath).Length == 0)
            document = PaiEditorViewModel.CreateNew(Path.GetFileNameWithoutExtension(filePath));
        else
            document = PaiJson.Load(filePath);
        viewModel.Load(document);
        RefreshJsonEditors();
    }

    public bool TrySaveFile(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath)) return false;
        if (!viewModel.CanSave)
        {
            MessageBox.Show("Fix the .pai structural errors before saving.", "Polaris AI Tree",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
        PaiJson.Save(filePath, viewModel.Document);
        path = filePath;
        viewModel.ClearDirty();
        return true;
    }

    void RefreshJsonEditors()
    {
        PortsText.Text = viewModel.SelectedNode == null ? "{}" :
            JsonSerializer.Serialize(viewModel.SelectedNode.Model.Ports, JsonOptions);
        AttributesText.Text = JsonSerializer.Serialize(viewModel.Document.BehaviorAttributes, JsonOptions);
    }

    static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions { WriteIndented = true };

    void AddNode_Click(object sender, RoutedEventArgs e) => AddSelectedCatalogNode();
    void CatalogList_MouseDoubleClick(object sender, MouseButtonEventArgs e) => AddSelectedCatalogNode();
    void AddSelectedCatalogNode()
    {
        if (CatalogList.SelectedItem is PaiNodeDescriptor descriptor) viewModel.AddNode(descriptor);
    }
    void DeleteNode_Click(object sender, RoutedEventArgs e) => viewModel.DeleteSelectedBranch();
    void DetachNode_Click(object sender, RoutedEventArgs e) => viewModel.DetachSelected();
    void MoveUp_Click(object sender, RoutedEventArgs e) => viewModel.MoveSelected(-1);
    void MoveDown_Click(object sender, RoutedEventArgs e) => viewModel.MoveSelected(1);
    void AutoLayout_Click(object sender, RoutedEventArgs e) => viewModel.AutoLayout();
    void Undo_Click(object sender, RoutedEventArgs e) => viewModel.Undo();
    void Redo_Click(object sender, RoutedEventArgs e) => viewModel.Redo();
    void Save_Click(object sender, RoutedEventArgs e) { if (path != null) TrySaveFile(path); }

    void Node_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is PaiNodeViewModel node) viewModel.SelectedNode = node;
    }

    void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => viewModel.FilterCatalog(SearchBox.Text);

    void ApplyPorts_Click(object sender, RoutedEventArgs e)
    {
        if (viewModel.SelectedNode == null) return;
        try
        {
            using JsonDocument json = JsonDocument.Parse(PortsText.Text);
            if (json.RootElement.ValueKind != JsonValueKind.Object) throw new JsonException("Ports must be a JSON object.");
            var ports = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (JsonProperty property in json.RootElement.EnumerateObject()) ports[property.Name] = property.Value.Clone();
            viewModel.SetPorts(viewModel.SelectedNode, ports);
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Invalid ports", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    void ApplyAttributes_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Dictionary<string, PaiBehaviorAttribute>? attributes =
                JsonSerializer.Deserialize<Dictionary<string, PaiBehaviorAttribute>>(AttributesText.Text);
            viewModel.SetBehaviorAttributes(attributes ?? new Dictionary<string, PaiBehaviorAttribute>(StringComparer.Ordinal));
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Invalid behavior attributes", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }
}

public sealed class PaiEditorViewModel : NotifyObject
{
    readonly PaiNodeCatalog catalog = PaiNodeCatalog.CreateBuiltIn();
    readonly Stack<string> undo = new Stack<string>();
    readonly Stack<string> redo = new Stack<string>();
    bool suppress;
    PaiNodeViewModel? selectedNode;

    public PaiEditorViewModel()
    {
        PendingConnection = new PaiPendingConnection(this);
        FilterCatalog(string.Empty);
    }

    public PaiDocument Document { get; private set; } = CreateNew("behavior");
    public ObservableCollection<PaiNodeDescriptor> Catalog { get; } = new ObservableCollection<PaiNodeDescriptor>();
    public ObservableCollection<PaiNodeViewModel> Nodes { get; } = new ObservableCollection<PaiNodeViewModel>();
    public ObservableCollection<PaiConnectionViewModel> Connections { get; } = new ObservableCollection<PaiConnectionViewModel>();
    public ObservableCollection<string> Diagnostics { get; } = new ObservableCollection<string>();
    public PaiPendingConnection PendingConnection { get; }
    public bool IsDirty { get; private set; }
    public bool CanSave => Diagnostics.All(x => !x.StartsWith("ERROR ", StringComparison.Ordinal));
    public event EventHandler? DirtyChanged;
    public event EventHandler? SelectionChanged;

    public PaiNodeViewModel? SelectedNode
    {
        get => selectedNode;
        set
        {
            if (ReferenceEquals(selectedNode, value)) return;
            selectedNode = value;
            OnPropertyChanged();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public static PaiDocument CreateNew(string id)
    {
        var document = new PaiDocument { Id = string.IsNullOrWhiteSpace(id) ? "behavior" : id, MainTree = "main" };
        var root = new PaiNode { Id = "root", Type = "Sequence", Name = "Root" };
        document.Trees.Add(new PaiTree { Id = "main", Root = root.Id, Nodes = new List<PaiNode> { root } });
        document.Editor.Nodes[root.Id] = new PaiNodeLayout { X = 80, Y = 80 };
        return document;
    }

    public void Load(PaiDocument document)
    {
        Document = document;
        undo.Clear();
        redo.Clear();
        IsDirty = false;
        Rebuild();
        DirtyChanged?.Invoke(this, EventArgs.Empty);
    }

    public void FilterCatalog(string? search)
    {
        string text = search ?? string.Empty;
        Catalog.Clear();
        foreach (PaiNodeDescriptor descriptor in catalog.Descriptors)
            if (descriptor.Type.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0 ||
                descriptor.DisplayName.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0)
                Catalog.Add(descriptor);
    }

    public void AddNode(PaiNodeDescriptor descriptor)
    {
        Mutate(() =>
        {
            PaiTree tree = MainTree;
            string stem = descriptor.Type.ToLowerInvariant();
            int suffix = 1;
            string id;
            do id = stem + "_" + suffix++; while (tree.Nodes.Any(x => x.Id == id));
            var node = new PaiNode { Id = id, Type = descriptor.Type, Name = descriptor.DisplayName };
            tree.Nodes.Add(node);
            Document.Editor.Nodes[id] = new PaiNodeLayout { X = 160 + tree.Nodes.Count * 16, Y = 160 + tree.Nodes.Count * 12 };
            selectedNode = null;
        });
        SelectedNode = Nodes.LastOrDefault();
    }

    public void DeleteSelectedBranch()
    {
        if (SelectedNode == null || SelectedNode.Model.Id == MainTree.Root) return;
        string selectedId = SelectedNode.Model.Id;
        Mutate(() =>
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            Collect(selectedId, ids);
            foreach (PaiNode node in MainTree.Nodes) node.Children.RemoveAll(ids.Contains);
            MainTree.Nodes.RemoveAll(x => ids.Contains(x.Id));
            foreach (string id in ids) Document.Editor.Nodes.Remove(id);
            selectedNode = null;
        });
    }

    public void DetachSelected()
    {
        if (SelectedNode == null) return;
        string id = SelectedNode.Model.Id;
        Mutate(() => { foreach (PaiNode node in MainTree.Nodes) node.Children.Remove(id); });
    }

    public void MoveSelected(int delta)
    {
        if (SelectedNode == null) return;
        string id = SelectedNode.Model.Id;
        PaiNode? parent = MainTree.Nodes.FirstOrDefault(x => x.Children.Contains(id));
        if (parent == null) return;
        int oldIndex = parent.Children.IndexOf(id);
        int newIndex = Math.Max(0, Math.Min(parent.Children.Count - 1, oldIndex + delta));
        if (oldIndex == newIndex) return;
        Mutate(() => { parent.Children.RemoveAt(oldIndex); parent.Children.Insert(newIndex, id); });
    }

    public bool TryConnect(PaiConnectorViewModel first, PaiConnectorViewModel second)
    {
        PaiConnectorViewModel? output = first.IsInput ? second : first;
        PaiConnectorViewModel? input = first.IsInput ? first : second;
        if (output.IsInput || !input.IsInput || ReferenceEquals(output.Owner, input.Owner)) return false;
        string parentId = output.Owner.Model.Id;
        string childId = input.Owner.Model.Id;
        PaiNode parent = output.Owner.Model;
        if (MainTree.Nodes.Any(x => x.Children.Contains(childId))) return false;
        if (!catalog.TryGet(parent.Type, out PaiNodeDescriptor descriptor)) return false;
        if (descriptor.Kind != PaiNodeKind.Composite && descriptor.Kind != PaiNodeKind.Decorator) return false;
        if (descriptor.Kind == PaiNodeKind.Decorator && parent.Children.Count != 0) return false;
        var descendants = new HashSet<string>(StringComparer.Ordinal);
        Collect(childId, descendants);
        if (descendants.Contains(parentId)) return false;
        Mutate(() => parent.Children.Add(childId));
        return true;
    }

    public void SetPorts(PaiNodeViewModel node, Dictionary<string, JsonElement> ports)
        => Mutate(() => node.Model.Ports = ports);

    public void SetBehaviorAttributes(Dictionary<string, PaiBehaviorAttribute> attributes)
        => Mutate(() => Document.BehaviorAttributes = new Dictionary<string, PaiBehaviorAttribute>(attributes, StringComparer.Ordinal));

    internal void Rename(PaiNodeViewModel node, string? name)
        => Mutate(() => node.Model.Name = string.IsNullOrWhiteSpace(name) ? null : name);

    internal void SetLocation(PaiNodeViewModel node, Point location)
    {
        if (suppress) return;
        Document.Editor.Nodes[node.Model.Id] = new PaiNodeLayout
        {
            X = Math.Round(location.X),
            Y = Math.Round(location.Y),
            Collapsed = Document.Editor.Nodes.TryGetValue(node.Model.Id, out PaiNodeLayout layout) && layout.Collapsed,
        };
        MarkDirty();
    }

    public void AutoLayout()
    {
        PushUndo();
        var placed = new HashSet<string>(StringComparer.Ordinal);
        int row = 0;
        void Place(string id, int depth)
        {
            if (!placed.Add(id)) return;
            Document.Editor.Nodes[id] = new PaiNodeLayout { X = 80 + depth * 240, Y = 60 + row++ * 110 };
            PaiNode? node = MainTree.Nodes.FirstOrDefault(x => x.Id == id);
            if (node != null) foreach (string child in node.Children) Place(child, depth + 1);
        }
        Place(MainTree.Root, 0);
        foreach (PaiNode node in MainTree.Nodes) Place(node.Id, 0);
        MarkDirty();
        Rebuild();
    }

    public void Undo()
    {
        if (undo.Count == 0) return;
        redo.Push(PaiJson.Serialize(Document));
        Document = PaiJson.Parse(undo.Pop());
        MarkDirty();
        Rebuild();
    }

    public void Redo()
    {
        if (redo.Count == 0) return;
        undo.Push(PaiJson.Serialize(Document));
        Document = PaiJson.Parse(redo.Pop());
        MarkDirty();
        Rebuild();
    }

    public void ClearDirty()
    {
        IsDirty = false;
        undo.Clear();
        redo.Clear();
        DirtyChanged?.Invoke(this, EventArgs.Empty);
    }

    PaiTree MainTree => Document.Trees.First(x => x.Id == Document.MainTree);

    void Mutate(Action mutation)
    {
        PushUndo();
        mutation();
        MarkDirty();
        Rebuild();
    }

    void PushUndo()
    {
        undo.Push(PaiJson.Serialize(Document));
        redo.Clear();
    }

    void MarkDirty()
    {
        if (IsDirty) return;
        IsDirty = true;
        DirtyChanged?.Invoke(this, EventArgs.Empty);
    }

    void Collect(string id, HashSet<string> result)
    {
        if (!result.Add(id)) return;
        PaiNode? node = MainTree.Nodes.FirstOrDefault(x => x.Id == id);
        if (node != null) foreach (string child in node.Children) Collect(child, result);
    }

    void Rebuild()
    {
        string? selectedId = selectedNode?.Model.Id;
        suppress = true;
        try
        {
            Nodes.Clear();
            Connections.Clear();
            var byId = new Dictionary<string, PaiNodeViewModel>(StringComparer.Ordinal);
            foreach (PaiNode model in MainTree.Nodes)
            {
                catalog.TryGet(model.Type, out PaiNodeDescriptor descriptor);
                bool canParent = descriptor != null && (descriptor.Kind == PaiNodeKind.Composite || descriptor.Kind == PaiNodeKind.Decorator);
                Document.Editor.Nodes.TryGetValue(model.Id, out PaiNodeLayout layout);
                var node = new PaiNodeViewModel(this, model, new Point(layout?.X ?? 80, layout?.Y ?? 80), canParent);
                Nodes.Add(node);
                byId[model.Id] = node;
            }
            foreach (PaiNode model in MainTree.Nodes)
            {
                if (!byId.TryGetValue(model.Id, out PaiNodeViewModel source) || source.Outputs.Count == 0) continue;
                foreach (string child in model.Children)
                {
                    if (!byId.TryGetValue(child, out PaiNodeViewModel target)) continue;
                    source.Outputs[0].IsConnected = true;
                    target.Inputs[0].IsConnected = true;
                    Connections.Add(new PaiConnectionViewModel(source.Outputs[0], target.Inputs[0]));
                }
            }
            selectedNode = selectedId == null ? null : Nodes.FirstOrDefault(x => x.Model.Id == selectedId);
            OnPropertyChanged(nameof(SelectedNode));
            RefreshDiagnostics();
        }
        finally { suppress = false; }
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    void RefreshDiagnostics()
    {
        Diagnostics.Clear();
        foreach (PaiDiagnostic diagnostic in PaiValidator.Validate(Document, catalog))
            Diagnostics.Add((diagnostic.Severity == PaiDiagnosticSeverity.Error ? "ERROR " : "WARN  ") + diagnostic);
    }
}

public sealed class PaiNodeViewModel : NotifyObject
{
    readonly PaiEditorViewModel owner;
    Point location;
    public PaiNodeViewModel(PaiEditorViewModel owner, PaiNode model, Point location, bool canParent)
    {
        this.owner = owner;
        Model = model;
        this.location = location;
        Inputs.Add(new PaiConnectorViewModel(this, true));
        if (canParent) Outputs.Add(new PaiConnectorViewModel(this, false));
    }
    public PaiNode Model { get; }
    public string Title => string.IsNullOrWhiteSpace(Model.Name) ? Model.Type : Model.Name + " · " + Model.Type;
    public string? Name { get => Model.Name; set { if (value != Model.Name) owner.Rename(this, value); } }
    public Point Location { get => location; set { if (location == value) return; location = value; OnPropertyChanged(); owner.SetLocation(this, value); } }
    public ObservableCollection<PaiConnectorViewModel> Inputs { get; } = new ObservableCollection<PaiConnectorViewModel>();
    public ObservableCollection<PaiConnectorViewModel> Outputs { get; } = new ObservableCollection<PaiConnectorViewModel>();
}

public sealed class PaiConnectorViewModel : NotifyObject
{
    Point anchor;
    bool connected;
    public PaiConnectorViewModel(PaiNodeViewModel owner, bool input) { Owner = owner; IsInput = input; }
    public PaiNodeViewModel Owner { get; }
    public bool IsInput { get; }
    public Point Anchor { get => anchor; set { anchor = value; OnPropertyChanged(); } }
    public bool IsConnected { get => connected; set { connected = value; OnPropertyChanged(); } }
}

public sealed class PaiConnectionViewModel
{
    public PaiConnectionViewModel(PaiConnectorViewModel source, PaiConnectorViewModel target) { Source = source; Target = target; }
    public PaiConnectorViewModel Source { get; }
    public PaiConnectorViewModel Target { get; }
}

public sealed class PaiPendingConnection
{
    PaiConnectorViewModel? source;
    public PaiPendingConnection(PaiEditorViewModel owner)
    {
        StartCommand = new DelegateCommand(value => source = value as PaiConnectorViewModel);
        FinishCommand = new DelegateCommand(value =>
        {
            if (source != null && value is PaiConnectorViewModel target) owner.TryConnect(source, target);
            source = null;
        });
    }
    public ICommand StartCommand { get; }
    public ICommand FinishCommand { get; }
}

public sealed class DelegateCommand : ICommand
{
    readonly Action<object?> execute;
    public DelegateCommand(Action<object?> execute) { this.execute = execute; }
    public bool CanExecute(object? parameter) => true;
    public void Execute(object? parameter) => execute(parameter);
#pragma warning disable CS0067
    public event EventHandler? CanExecuteChanged;
#pragma warning restore CS0067
}

public abstract class NotifyObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
