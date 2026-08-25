using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using PolarisTools.Res;

namespace PolarisTools.Pui.PuiVisualEditor.Controls
{
    /// <summary>
    /// Custom 元素的后端类型选择器：把当前 <c>.pui</c> 所属项目里全部实现了
    /// <c>Polaris.PUI.IPuiCustomControl</c> 的具体类型列成一个带搜索框的下拉
    /// （见 <see cref="PolarisCustomControlCatalog"/>），选中的那个完整类型引用存进
    /// <see cref="PuiElement.BackendType"/>。跟 <see cref="PuiResourcePicker"/> 是同一套
    /// "自绘按钮 + Popup（搜索框 + 列表）"实现，只是没有缩略图。
    /// </summary>
    public partial class PuiCustomControlPicker : UserControl
    {
        public static readonly DependencyProperty SelectedTypeProperty = DependencyProperty.Register(
            nameof(SelectedType), typeof(string), typeof(PuiCustomControlPicker),
            new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnSelectedTypeChanged));

        /// <summary>正在编辑的 <c>.pui</c> 路径：用来定位所属项目、拿到那个项目的后端类型清单。</summary>
        public static readonly DependencyProperty SourceFilePathProperty = DependencyProperty.Register(
            nameof(SourceFilePath), typeof(string), typeof(PuiCustomControlPicker),
            new FrameworkPropertyMetadata(null, OnSourceFilePathChanged));

        public string SelectedType
        {
            get => (string)GetValue(SelectedTypeProperty);
            set => SetValue(SelectedTypeProperty, value);
        }

        public string SourceFilePath
        {
            get => (string)GetValue(SourceFilePathProperty);
            set => SetValue(SourceFilePathProperty, value);
        }

        /// <summary>只在用户真的在下拉里选了/清空了类型时触发，跟 <see cref="PuiResourcePicker.SelectedReferenceChanged"/> 是同一套约定。</summary>
        public event EventHandler SelectedTypeChanged;

        private readonly ObservableCollection<TypeImplementation> _results = new ObservableCollection<TypeImplementation>();
        private PolarisCustomControlCatalog _catalog;
        private bool _committing;

        public PuiCustomControlPicker()
        {
            InitializeComponent();
            ResultList.ItemsSource = _results;
            Loaded += (s, e) => UpdateDisplay();
        }

        private static void OnSelectedTypeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var picker = (PuiCustomControlPicker)d;
            if (picker._committing)
                picker.SelectedTypeChanged?.Invoke(picker, EventArgs.Empty);
            else
                picker.UpdateDisplay();
        }

        private static void OnSourceFilePathChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
            => ((PuiCustomControlPicker)d).RebindCatalog(e.NewValue as string);

        private void RebindCatalog(string puiFilePath)
        {
            if (_catalog != null)
                _catalog.Changed -= Catalog_Changed;

            _catalog = string.IsNullOrEmpty(puiFilePath) ? null : PolarisCustomControlCatalog.ForPuiFile(puiFilePath);

            if (_catalog != null)
                _catalog.Changed += Catalog_Changed;

            UpdateDisplay();
        }

        // FileSystemWatcher 的回调跑在线程池线程上，不能直接碰 WPF 对象。清单本身是惰性重扫的，
        // 这里只需要让当前显示/展开中的列表重新取一次。
        private void Catalog_Changed(object sender, EventArgs e)
            => Dispatcher.BeginInvoke(new Action(() =>
            {
                UpdateDisplay();
                if (DropDown.IsOpen)
                    ApplyFilter();
            }));

        /// <summary>
        /// 收起状态的显示：能在清单里找到就显示短名；找不到但类型引用非空，说明类型被
        /// 改名/删了（或者是手写进来的一个还没找到的类名）——原样显示引用并标一个 ⚠，
        /// 绝不悄悄清掉用户填的值。
        /// </summary>
        private void UpdateDisplay()
        {
            string qualifiedName = SelectedType ?? "";
            ClearButton.Visibility = qualifiedName.Length == 0 ? Visibility.Collapsed : Visibility.Visible;

            if (qualifiedName.Length == 0)
            {
                SelectedText.Text = "(none)";
                SelectedText.ToolTip = null;
                return;
            }

            if (_catalog != null && _catalog.TryGet(qualifiedName, out TypeImplementation type))
            {
                SelectedText.Text = type.DisplayName;
                SelectedText.ToolTip = type.QualifiedName;
                return;
            }

            SelectedText.Text = "⚠ " + qualifiedName;
            SelectedText.ToolTip = "This type was not found in the project (or the project could not be scanned yet). It may have been renamed, removed, or not implement Polaris.UI.IPuiCustomControl.";
        }

        private void OpenButton_Click(object sender, RoutedEventArgs e) => DropDown.IsOpen = !DropDown.IsOpen;

        private void DropDown_Opened(object sender, EventArgs e)
        {
            SearchBox.Text = "";
            ApplyFilter();
            SelectCurrentInList();
            // Popup 刚 Opened 时内容还没进入焦点范围，就地 Focus() 会落空（WPF Popup 的老毛病），
            // 推到下一拍输入优先级再抢一次。
            Dispatcher.BeginInvoke(new Action(() => SearchBox.Focus()), DispatcherPriority.Input);
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => ApplyFilter();

        private void ApplyFilter()
        {
            string[] terms = SplitTerms(SearchBox.Text);
            IReadOnlyList<TypeImplementation> all = _catalog?.Types ?? new List<TypeImplementation>();

            _results.Clear();
            foreach (TypeImplementation type in all)
            {
                if (Matches(type, terms))
                    _results.Add(type);
            }

            if (_results.Count > 0)
            {
                EmptyHint.Visibility = Visibility.Collapsed;
                if (ResultList.SelectedIndex < 0)
                    ResultList.SelectedIndex = 0;
                return;
            }

            EmptyHint.Visibility = Visibility.Visible;
            EmptyHint.Text = all.Count == 0
                ? "This project has no class implementing Polaris.UI.IPuiCustomControl yet. Write one (public, non-abstract, non-generic, with a public parameterless constructor) and it will show up here."
                : "No type matches the search.";
        }

        private static bool Matches(TypeImplementation type, string[] terms)
        {
            if (terms == null)
                return true;

            string haystack = (type.QualifiedName + " " + type.DisplayName).ToLowerInvariant();
            foreach (string term in terms)
            {
                if (haystack.IndexOf(term, StringComparison.Ordinal) < 0)
                    return false;
            }
            return true;
        }

        private static string[] SplitTerms(string search)
        {
            if (string.IsNullOrWhiteSpace(search))
                return null;
            return search.ToLowerInvariant().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        }

        private void SelectCurrentInList()
        {
            string qualifiedName = SelectedType ?? "";
            for (int i = 0; i < _results.Count; i++)
            {
                if (string.Equals(_results[i].QualifiedName, qualifiedName, StringComparison.Ordinal))
                {
                    ResultList.SelectedIndex = i;
                    ResultList.ScrollIntoView(_results[i]);
                    return;
                }
            }
        }

        // 鼠标点选走 PreviewMouseLeftButtonUp 而不是 SelectionChanged：后者在键盘上下键移动高亮时
        // 也会触发，那样每按一下方向键都会往文档里写一次值。
        private void ResultList_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (ItemUnder(e.OriginalSource as DependencyObject) is TypeImplementation type)
            {
                Commit(type.QualifiedName);
                e.Handled = true;
            }
        }

        private static object ItemUnder(DependencyObject source)
        {
            while (source != null && !(source is ListBoxItem))
                source = System.Windows.Media.VisualTreeHelper.GetParent(source);
            return (source as ListBoxItem)?.DataContext;
        }

        private void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Down:
                    MoveSelection(1);
                    e.Handled = true;
                    break;
                case Key.Up:
                    MoveSelection(-1);
                    e.Handled = true;
                    break;
                case Key.Enter:
                    CommitSelected();
                    e.Handled = true;
                    break;
                case Key.Escape:
                    DropDown.IsOpen = false;
                    e.Handled = true;
                    break;
            }
        }

        private void ResultList_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                CommitSelected();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                DropDown.IsOpen = false;
                e.Handled = true;
            }
        }

        private void MoveSelection(int delta)
        {
            if (_results.Count == 0)
                return;

            int index = ResultList.SelectedIndex + delta;
            if (index < 0) index = 0;
            if (index >= _results.Count) index = _results.Count - 1;
            ResultList.SelectedIndex = index;
            ResultList.ScrollIntoView(_results[index]);
        }

        private void CommitSelected()
        {
            if (ResultList.SelectedItem is TypeImplementation type)
                Commit(type.QualifiedName);
        }

        private void ClearButton_Click(object sender, RoutedEventArgs e) => Commit("");

        private void Commit(string qualifiedName)
        {
            DropDown.IsOpen = false;

            if (string.Equals(SelectedType ?? "", qualifiedName, StringComparison.Ordinal))
                return;

            _committing = true;
            try
            {
                SetCurrentValue(SelectedTypeProperty, qualifiedName);
                UpdateDisplay();
            }
            finally
            {
                _committing = false;
            }
        }
    }
}
