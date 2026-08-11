using Polaris.PUI.Wire;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GongSolutions.Wpf.DragDrop;
using Microsoft.VisualStudio.Shell;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Input;
using System.Xml.Linq;

namespace PolarisTools.Pui.PuiVisualEditor
{
    public partial class PuiVisualEditorViewModel : ObservableObject
    {
        [ObservableProperty]
        private PuiElement _rootElement;

        [ObservableProperty]
        private PuiElement _selectedElement;

        [ObservableProperty]
        private PuiLineInfo _selectedLine;

        [ObservableProperty]
        private string _xmlSource = "";

        [ObservableProperty]
        private string _filePath;

        [ObservableProperty]
        private bool _isDirty;

        private readonly Stack<string> _undoStack = new Stack<string>();
        private readonly Stack<string> _redoStack = new Stack<string>();
        private bool _isUndoRedoing;

        public ObservableCollection<ToolboxGroup> ToolboxGroups { get; } = new ObservableCollection<ToolboxGroup>
        {
            new ToolboxGroup
            {
                GroupName = "基本控件",
                Items = new ObservableCollection<ToolboxItem>
                {
                    new ToolboxItem { Type = PuiElementType.Button, DisplayName = "Button" },
                    new ToolboxItem { Type = PuiElementType.Text, DisplayName = "Text" },
                    new ToolboxItem { Type = PuiElementType.LineBreak, DisplayName = "换行" },
                    new ToolboxItem { Type = PuiElementType.Separator, DisplayName = "分割线" }
                }
            },
            new ToolboxGroup
            {
                GroupName = "选择与列表",
                Items = new ObservableCollection<ToolboxItem>
                {
                    new ToolboxItem { Type = PuiElementType.ButtonMulti, DisplayName = "多按钮" },
                    new ToolboxItem { Type = PuiElementType.Checks, DisplayName = "复选框组" },
                    new ToolboxItem { Type = PuiElementType.Radio, DisplayName = "单选组" }
                }
            },
            new ToolboxGroup
            {
                GroupName = "数值与输入",
                Items = new ObservableCollection<ToolboxItem>
                {
                    new ToolboxItem { Type = PuiElementType.Slider, DisplayName = "滑块" },
                    new ToolboxItem { Type = PuiElementType.Input, DisplayName = "输入框" },
                    new ToolboxItem { Type = PuiElementType.NumCounter, DisplayName = "数字计数器" }
                }
            },
            new ToolboxGroup
            {
                GroupName = "图像与颜色",
                Items = new ObservableCollection<ToolboxItem>
                {
                    new ToolboxItem { Type = PuiElementType.Image, DisplayName = "图像" },
                    new ToolboxItem { Type = PuiElementType.ColorCell, DisplayName = "颜色格" }
                }
            }
        };

        public ICommand AddElementCommand { get; }
        public ICommand DeleteElementCommand { get; }
        public ICommand UndoCommand { get; }
        public ICommand RedoCommand { get; }

        public bool CanUndo => _undoStack.Count > 0;
        public bool CanRedo => _redoStack.Count > 0;

        public ObservableCollection<PuiLineInfo> Lines { get; } = new ObservableCollection<PuiLineInfo>();

        public IDropTarget LineDropHandler { get; }

        public PuiVisualEditorViewModel()
        {
            AddElementCommand = new RelayCommand<PuiElementType>(AddElement);
            DeleteElementCommand = new RelayCommand<PuiElement>(DeleteElement, e => e != null && e != RootElement);
            UndoCommand = new RelayCommand(Undo, () => CanUndo);
            RedoCommand = new RelayCommand(Redo, () => CanRedo);
            LineDropHandler = new PuiLineDropHandler(this);
            CreateNewDocument();
        }

        partial void OnSelectedElementChanging(PuiElement oldValue, PuiElement newValue)
        {
            if (oldValue != null) oldValue.IsSelected = false;
        }

        partial void OnSelectedElementChanged(PuiElement value)
        {
            if (value != null)
            {
                value.IsSelected = true;
                SelectedLine = null;
            }
        }

        partial void OnRootElementChanged(PuiElement value)
        {
            SelectedLine = null;
            RefreshLines();
            UpdateXmlSource();
        }

        partial void OnSelectedLineChanging(PuiLineInfo oldValue, PuiLineInfo newValue)
        {
            if (oldValue != null)
                foreach (var el in oldValue.Elements)
                    el.IsLineSelected = false;
        }

        partial void OnSelectedLineChanged(PuiLineInfo value)
        {
            if (value != null)
            {
                foreach (var el in value.Elements)
                    el.IsLineSelected = true;
                SelectedElement = null;
            }
            OnPropertyChanged(nameof(SelectedLineAlign));
        }

        public PuiLineAlign SelectedLineAlign
        {
            get => SelectedLine?.Align ?? PuiLineAlign.Left;
            set
            {
                if (SelectedLine != null)
                    SetLineAlign(SelectedLine, value);
            }
        }

        public void RefreshLines()
        {
            Lines.Clear();
            if (RootElement == null) return;
            CollapseRedundantMarkers();
            foreach (var line in PuiPreviewRenderer.ComputeWindowLines(RootElement))
                Lines.Add(line);
        }

        // Collapses runs of consecutive LineStyle/DefaultLineStyle markers down to the last one
        // (only the last matters, state-machine style), and drops that last one too if it doesn't
        // actually change the align that was already in effect before it.
        private void CollapseRedundantMarkers()
        {
            var children = RootElement.Children;
            var toRemove = new List<PuiElement>();
            var runningAlign = PuiLineAlign.Left;
            int i = 0;
            while (i < children.Count)
            {
                if (!PuiElement.IsMarker(children[i].ElementType))
                {
                    i++;
                    continue;
                }

                int j = i;
                while (j < children.Count && PuiElement.IsMarker(children[j].ElementType))
                    j++;

                for (int k = i; k < j - 1; k++)
                    toRemove.Add(children[k]);

                var last = children[j - 1];
                var lastAlign = last.ElementType == PuiElementType.DefaultLineStyle ? PuiLineAlign.Left : last.LineAlign;
                if (lastAlign == runningAlign)
                    toRemove.Add(last);
                else
                    runningAlign = lastAlign;

                i = j;
            }

            foreach (var el in toRemove)
                children.Remove(el);
        }

        public void SetLineAlign(PuiLineInfo line, PuiLineAlign align)
        {
            if (line == null || RootElement == null || line.Align == align) return;
            SaveUndoState();

            var nextLine = PuiPreviewRenderer.ComputeWindowLines(RootElement)
                .FirstOrDefault(l => l.StartIndex > line.EndIndex);
            if (nextLine != null)
            {
                if (nextLine.LeadingMarker == null)
                    EnsureExplicitMarker(nextLine, nextLine.Align);
            }
            else if (align != PuiLineAlign.Left && line.Elements.Count > 0)
            {
                // 这一行目前是文档最后一行：如果不在它后面补一个 DefaultLineStyle 把状态收回 Left，
                // 这次设的 align 会变成"悬空"的状态一直持续下去——用户以后随手拖一个新控件进来，
                // 会因为继承了这个从未被重置的状态而莫名其妙跟着居中（对应"按钮组永远居中"的反馈）。
                var lastElement = line.Elements[line.Elements.Count - 1];
                int resetAt = RootElement.Children.IndexOf(lastElement) + 1;
                RootElement.Children.Insert(resetAt, new PuiElement(PuiElementType.DefaultLineStyle) { Parent = RootElement });
            }

            var children = RootElement.Children;
            if (line.LeadingMarker != null)
            {
                int idx = children.IndexOf(line.LeadingMarker);
                children.RemoveAt(idx);
                children.Insert(idx, CreateMarker(align));
            }
            else
            {
                children.Insert(line.StartIndex, CreateMarker(align));
            }

            SelectedLine = null;
            RefreshLines();
            UpdateXmlSource();
            IsDirty = true;
        }

        public void MoveLine(PuiLineInfo source, int targetIndexInLines)
        {
            if (source == null || RootElement == null || source.Elements.Count == 0) return;
            var linesSnapshot = Lines.ToList();
            int sourceIndex = linesSnapshot.IndexOf(source);
            if (sourceIndex < 0) return;
            if (targetIndexInLines == sourceIndex || targetIndexInLines == sourceIndex + 1) return;

            SaveUndoState();
            SelectedLine = null;

            var nextLine = linesSnapshot.ElementAtOrDefault(sourceIndex + 1);
            var targetLine = targetIndexInLines >= 0 && targetIndexInLines < linesSnapshot.Count
                ? linesSnapshot[targetIndexInLines]
                : null;

            // Make every line whose effective style would otherwise change as a side effect
            // of moving `source` explicit first, so the move is purely visual/positional.
            EnsureExplicitMarker(source, source.Align);
            if (nextLine != null) EnsureExplicitMarker(nextLine, nextLine.Align);
            if (targetLine != null) EnsureExplicitMarker(targetLine, targetLine.Align);

            var children = RootElement.Children;
            var moveSet = new List<PuiElement> { source.LeadingMarker };
            moveSet.AddRange(source.Elements);

            foreach (var el in moveSet)
                children.Remove(el);

            int insertAt = targetLine?.LeadingMarker != null
                ? children.IndexOf(targetLine.LeadingMarker)
                : children.Count;
            if (insertAt < 0) insertAt = children.Count;

            foreach (var el in moveSet)
                children.Insert(insertAt++, el);

            RefreshLines();
            UpdateXmlSource();
            IsDirty = true;
        }

        private void EnsureExplicitMarker(PuiLineInfo line, PuiLineAlign align)
        {
            if (line.LeadingMarker != null || line.Elements.Count == 0) return;
            var marker = CreateMarker(align);
            int insertAt = RootElement.Children.IndexOf(line.Elements[0]);
            RootElement.Children.Insert(insertAt, marker);
            line.LeadingMarker = marker;
        }

        private PuiElement CreateMarker(PuiLineAlign align)
        {
            if (align == PuiLineAlign.Left)
                return new PuiElement(PuiElementType.DefaultLineStyle) { Parent = RootElement };
            return new PuiElement(PuiElementType.LineStyle) { LineAlign = align, Parent = RootElement };
        }

        public void CreateNewDocument()
        {
            RootElement = new PuiElement(PuiElementType.Window);
            SelectedElement = RootElement;
            FilePath = null;
            IsDirty = false;
        }

        public void AddElement(PuiElementType type)
        {
            if (type == PuiElementType.Window) return;
            SaveUndoState();
            SelectedLine = null;

            var elem = new PuiElement(type) { Parent = RootElement };
            bool isLayoutElement = type == PuiElementType.LineBreak || type == PuiElementType.Separator;
            if (!isLayoutElement)
                elem.Name = $"{type}{RootElement.Children.Count + 1}";

            int insertIndex = RootElement.Children.Count;
            int selectedIndex = SelectedElement != null && SelectedElement != RootElement
                ? RootElement.Children.IndexOf(SelectedElement)
                : -1;
            if (selectedIndex >= 0)
                insertIndex = selectedIndex + 1;

            RootElement.Children.Insert(insertIndex, elem);
            if (!isLayoutElement)
                EnsureUniqueElementName(elem);
            SelectedElement = isLayoutElement ? RootElement : elem;
            RefreshLines();
            UpdateXmlSource();
            IsDirty = true;
        }

        // Ensures `element.Name` doesn't collide with any other named element under RootElement.
        // On collision, appends "_0", "_1", ... to the current name until it's unique.
        public void EnsureUniqueElementName(PuiElement element)
        {
            if (element == null || RootElement == null || string.IsNullOrEmpty(element.Name)) return;

            var otherNames = new HashSet<string>(
                RootElement.Children
                    .Where(c => c != element && !string.IsNullOrEmpty(c.Name))
                    .Select(c => c.Name));

            element.Name = MakeUnique(element.Name, otherNames);
        }

        // Returns a copy of `baseName` guaranteed not to collide with any callback method name
        // already bound (in any element's CallbackHooks) elsewhere in the document. On collision,
        // appends "_0", "_1", ... until unique — same convention as EnsureUniqueElementName — so two
        // unrelated callbacks never silently end up sharing one code-behind method just because their
        // auto-generated default names happened to match (e.g. two elements sharing a Name in
        // different branches, or a Window's OnBuildCompleted colliding with a child's OnClick).
        public string EnsureUniqueCallbackName(string baseName)
        {
            if (string.IsNullOrEmpty(baseName) || RootElement == null) return baseName;

            var boundNames = new HashSet<string>();
            void Collect(PuiElement e)
            {
                foreach (var hook in e.CallbackHooks)
                    if (hook.IsBound) boundNames.Add(hook.MethodName);
            }
            Collect(RootElement);
            foreach (var child in RootElement.Children)
                Collect(child);

            return MakeUnique(baseName, boundNames);
        }

        // 名字已被占用时追加 "_0"/"_1"/... 直到不冲突；没冲突则原样返回。
        // EnsureUniqueElementName（元素 Name）和 EnsureUniqueCallbackName（回调方法名）共用，
        // 两处的去重后缀规则因此不会各自漂移。
        private static string MakeUnique(string baseName, ICollection<string> taken)
        {
            if (!taken.Contains(baseName)) return baseName;

            for (int i = 0; ; i++)
            {
                string candidate = $"{baseName}_{i}";
                if (!taken.Contains(candidate)) return candidate;
            }
        }

        public void DeleteElement(PuiElement element)
        {
            if (element == null || element == RootElement) return;
            SaveUndoState();
            SelectedLine = null;
            element.Parent?.Children.Remove(element);
            SelectedElement = RootElement;
            RefreshLines();
            UpdateXmlSource();
            IsDirty = true;
        }

        private void SaveUndoState()
        {
            if (_isUndoRedoing) return;
            UpdateXmlSource();
            _undoStack.Push(XmlSource);
            _redoStack.Clear();
            OnPropertyChanged(nameof(CanUndo));
            OnPropertyChanged(nameof(CanRedo));
        }

        public void Undo() => ExecuteUndoRedo(_undoStack, _redoStack);

        public void Redo() => ExecuteUndoRedo(_redoStack, _undoStack);

        private void ExecuteUndoRedo(Stack<string> fromStack, Stack<string> toStack)
        {
            if (fromStack.Count == 0) return;
            _isUndoRedoing = true;
            try
            {
                UpdateXmlSource();
                toStack.Push(XmlSource);
                XmlSource = fromStack.Pop();
                TryParseXmlSource();
                OnPropertyChanged(nameof(CanUndo));
                OnPropertyChanged(nameof(CanRedo));
            }
            finally
            {
                _isUndoRedoing = false;
            }
        }

        public void UpdateXmlSource()
        {
            if (RootElement == null)
            {
                XmlSource = "";
                return;
            }
            var doc = new XDocument(new XDeclaration("1.0", "utf-8", null), RootElement.ToXml());
            XmlSource = doc.ToString();
        }

        public bool TryParseXmlSource()
        {
            try
            {
                var doc = XDocument.Parse(XmlSource);
                var root = PuiElement.FromXml(doc.Root);
                if (root != null && root.ElementType == PuiElementType.Window)
                {
                    RootElement = root;
                    SelectedElement = root;
                    IsDirty = true;
                    return true;
                }
            }
            catch { }
            return false;
        }

        public void LoadFromFile(string path)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var content = File.ReadAllText(path, Encoding.UTF8);
            XmlSource = content;
            if (TryParseXmlSource())
            {
                FilePath = path;
                IsDirty = false;
            }
        }

        public void SaveToFile(string path)
        {
            UpdateXmlSource();
            File.WriteAllText(path, XmlSource, Encoding.UTF8);
            FilePath = path;
            IsDirty = false;
            PuiFileChangeNotifier.NotifySaved(path);
        }

        public void MarkDirty()
        {
            IsDirty = true;
            RefreshLines();
            UpdateXmlSource();
        }

        public void SaveStateBeforePropertyChange()
        {
            SaveUndoState();
        }
    }

    public class ToolboxGroup
    {
        public string GroupName { get; set; }
        public ObservableCollection<ToolboxItem> Items { get; set; } = new ObservableCollection<ToolboxItem>();
    }

    public partial class ToolboxItem : ObservableObject
    {
        public PuiElementType Type { get; set; }
        public string DisplayName { get; set; }
    }
}
