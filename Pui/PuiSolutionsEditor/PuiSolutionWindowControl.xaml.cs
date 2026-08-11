using EnvDTE;
using Microsoft.VisualStudio.Shell;
using Microsoft.Win32;
using PolarisTools.Pui.PuiSolutions.ViewModel;
using PolarisTools.Pui.PuiVisualEditor;
using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace PolarisTools.Pui.PuiSolutions
{
    public partial class PuiSolutionWindowControl : UserControl
    {
        internal static PuiSolutionWindowControl PuiSolution;
        /// <summary>工作区：可编辑，默认无节点</summary>
        public EditorViewModel ViewModel { get; private set; }

        private readonly bool _isToolWindowHost;

        public PuiSolutionWindowControl(bool initGraph = true)
        {
            _isToolWindowHost = initGraph;

            LoadHandyControlStyles();
            InitializeComponent();

            ViewModel = new EditorViewModel();
            Editor.DataContext = ViewModel;
            DataContext = ViewModel;
            PuiSolution = this;
            if (!_isToolWindowHost)
                HideStartOverlay();

            PuiFileChangeNotifier.Saved += OnPuiFileSaved;
            Unloaded += (_, _) => PuiFileChangeNotifier.Saved -= OnPuiFileSaved;
        }

        /// <summary>某个 .pui 文件被保存后回调：本图里所有绑定到该文件的 PuiState 节点
        /// 都重新读取一遍，跟右键"刷新（重新读取 .pui）"是同一套逻辑，只是自动触发。</summary>
        private void OnPuiFileSaved(string path)
        {
            if (string.IsNullOrEmpty(path)) return;

            var affected = ViewModel.Nodes
                .Where(n => n.Type == NodeType.PuiState
                    && !string.IsNullOrEmpty(n.PuiFilePath)
                    && string.Equals(n.PuiFilePath, path, StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (NodeViewModel node in affected)
            {
                try { ViewModel.RefreshPuiStateNode(node); }
                catch { /* 保存流程不应该被某个节点的刷新失败打断 */ }
            }
        }

        private static (Point origin, double zoom)? GetViewportInfo(FrameworkElement editor)
        {
            var editorType = editor.GetType();
            if (editorType.GetProperty("ViewportLocation")?.GetValue(editor) is not Point origin)
                return null;
            var zoom = editorType.GetProperty("ViewportZoom")?.GetValue(editor) is double z && z > 0 ? z : 1.0;
            return (origin, zoom);
        }

        private void New_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.ClearGraph();
            HideStartOverlay();
        }

        public void LoadFromFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return;
            try
            {
                ViewModel.LoadFromFile(path);
                HideStartOverlay();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"加载失败：\n{ex.Message}", "PUI Manager",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        /// <summary>
        /// 空白处才弹出「添加节点」；已选 node / connection 则取消。空白处打开时顺便重新
        /// 扫描一次同目录下的 .pui 文件填充子菜单——每次右键都是新鲜扫描，不需要额外的
        /// "刷新目录"按钮。
        /// </summary>
        private void Editor_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            bool hasNodeSelection = Editor.SelectedItems?.Count > 0;
            bool hasConnSelection = ViewModel?.SelectedConnections?.Count > 0;
            if (hasNodeSelection || hasConnSelection)
            {
                e.Handled = true;
                return;
            }
            BuildAddNodeSubMenu();
        }

        /// <summary>
        /// 扫描本 .puisln 所在目录下的所有 .pui 文件，逐个建一个菜单项；点击即创建绑定到
        /// 该文件的 PUI 状态节点。尚未保存过（FilePath 为空，没有"所在目录"）时提示先保存。
        /// </summary>
        private void BuildAddNodeSubMenu()
        {
            AddNodeSubMenu.Items.Clear();

            if (string.IsNullOrEmpty(ViewModel.FilePath))
            {
                AddNodeSubMenu.Items.Add(new MenuItem { Header = "请先保存 .puisln 文件后再添加 PUI 状态节点", IsEnabled = false });
                return;
            }

            string dir = Path.GetDirectoryName(ViewModel.FilePath);
            string[] puiFiles = Directory.Exists(dir) ? Directory.GetFiles(dir, "*.pui") : Array.Empty<string>();
            if (puiFiles.Length == 0)
            {
                AddNodeSubMenu.Items.Add(new MenuItem { Header = "（此目录下没有找到 .pui 文件）", IsEnabled = false });
                return;
            }

            foreach (string path in puiFiles.OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
            {
                var mi = new MenuItem { Header = Path.GetFileNameWithoutExtension(path), Tag = path };
                mi.Click += AddPuiStateNodeMenuItem_Click;
                AddNodeSubMenu.Items.Add(mi);
            }
        }

        private void AddPuiStateNodeMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuItem mi || mi.Tag is not string puiPath)
                return;

            try
            {
                ViewModel.AddPuiStateNodeAt(puiPath, GetEditorAddLocation());
            }
            catch (Exception ex)
            {
                MessageBox.Show($"读取 .pui 失败：\n{ex.Message}", "PUI Manager",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void RefreshNode_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuItem mi || mi.DataContext is not NodeViewModel node)
                return;

            try
            {
                ViewModel.RefreshPuiStateNode(node);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"刷新失败：\n{ex.Message}", "PUI Manager",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void DeleteNode_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem mi && mi.DataContext is NodeViewModel node)
                ViewModel.DeleteNode(node);
        }

        /// <summary>Delete 键批量删除选中节点和选中连线；Entry/Exit 节点由 ViewModel.DeleteNode 自行拒绝，
        /// 这里不用额外过滤。</summary>
        private void Editor_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Delete) return;
            var nodes = Editor.SelectedItems?.OfType<NodeViewModel>().ToList();
            if (nodes != null && nodes.Count > 0)
            {
                foreach (NodeViewModel n in nodes)
                    ViewModel.DeleteNode(n);
            }
            ViewModel.DeleteSelectedConnections();
        }

        private Point GetEditorAddLocation()
        {
            try
            {
                var mouseLocProp = Editor.GetType().GetProperty("MouseLocation");
                if (mouseLocProp?.GetValue(Editor) is Point p)
                    return p;

                var info = GetViewportInfo(Editor);
                if (info != null)
                {
                    var pos = Mouse.GetPosition(Editor);
                    return new Point(info.Value.origin.X + pos.X / info.Value.zoom,
                                     info.Value.origin.Y + pos.Y / info.Value.zoom);
                }
            }
            catch { }

            int n = ViewModel.Nodes.Count;
            return new Point(80 + (n % 8) * 24, 80 + (n % 8) * 24);
        }

        private void LoadHandyControlStyles()
        {
            Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri(
                    "pack://application:,,,/HandyControl;component/Themes/SkinDefault.xaml",
                    UriKind.Absolute)
            });
            Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri(
                    "pack://application:,,,/HandyControl;component/Themes/Theme.xaml",
                    UriKind.Absolute)
            });
        }

        /// <summary>
        /// 工具窗口每次被打开时调用：重新显示「新建 / 打开现有」。
        /// 文档编辑器调用无效（被 _isToolWindowHost 挡住）。
        /// </summary>
        public void ShowStartOverlay()
        {
            if (_isToolWindowHost && StartOverlay != null)
                StartOverlay.Visibility = Visibility.Visible;
        }

        public void HideStartOverlay()
        {
            if (StartOverlay != null)
                StartOverlay.Visibility = Visibility.Collapsed;
        }

        private void Open_Click(object sender, RoutedEventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var dlg = new OpenFileDialog
            {
                Title = "打开 PUI 解决方案图",
                Filter = "PUI Solution (*.puisln)|*.puisln|所有文件 (*.*)|*.*",
                DefaultExt = ".puisln",
                CheckFileExists = true,
                Multiselect = false,
                InitialDirectory = GetDefaultDirectory(ViewModel.FilePath)
            };

            if (dlg.ShowDialog() != true)
                return;

            LoadFromFile(dlg.FileName);
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                if (!string.IsNullOrEmpty(ViewModel.FilePath))
                {
                    ViewModel.SaveToFile(ViewModel.FilePath);
                    return;
                }

                var dlg = new SaveFileDialog
                {
                    Title = "保存 PUI 解决方案图",
                    Filter = "PUI Solution (*.puisln)|*.puisln",
                    DefaultExt = ".puisln",
                    AddExtension = true,
                    FileName = "Untitled.puisln",
                    InitialDirectory = GetDefaultDirectory(null)
                };
                if (dlg.ShowDialog() != true)
                    return;

                ViewModel.SaveToFile(dlg.FileName);
                MessageBox.Show($"已保存：\n{dlg.FileName}", "PUI Manager",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"保存失败：\n{ex.Message}", "PUI Manager",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static string GetDefaultDirectory(string currentFile)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            // Try current file's directory first
            if (!string.IsNullOrEmpty(currentFile))
            {
                var dir = Path.GetDirectoryName(currentFile);
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                    return dir;
            }

            // Try project or solution directory
            try
            {
                var dte = Package.GetGlobalService(typeof(DTE)) as DTE;
                var projectDir = TryGetProjectDirectory(dte);
                if (projectDir != null) return projectDir;

                var solutionDir = TryGetSolutionDirectory(dte);
                if (solutionDir != null) return solutionDir;
            }
            catch { }

            return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        }

        private static string TryGetProjectDirectory(DTE dte)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (dte?.ActiveSolutionProjects is Array arr && arr.Length > 0
                && arr.GetValue(0) is Project proj
                && !string.IsNullOrEmpty(proj.FullName))
            {
                var dir = Path.GetDirectoryName(proj.FullName);
                if (Directory.Exists(dir)) return dir;
            }
            return null;
        }

        private static string TryGetSolutionDirectory(DTE dte)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (!string.IsNullOrEmpty(dte?.Solution?.FullName))
            {
                var dir = Path.GetDirectoryName(dte.Solution.FullName);
                if (Directory.Exists(dir)) return dir;
            }
            return null;
        }
    }
}