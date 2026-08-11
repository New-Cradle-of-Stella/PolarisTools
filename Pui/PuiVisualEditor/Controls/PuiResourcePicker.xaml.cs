using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace PolarisTools.Pui.PuiVisualEditor.Controls
{
    /// <summary>
    /// Image 元素的图片资源选择器：把当前 <c>.pui</c> 所属项目里全部
    /// <c>[PolarisResourceFolder]</c> 类里的 <c>[PolarisResource]</c> <c>MImage</c> static 字段
    /// 列成一个带搜索框的下拉（见 <see cref="PolarisResourceCatalog"/>），选中的那个字段引用
    /// 存进 <see cref="PuiElement.ImageResource"/>。
    /// <para>
    /// 用自绘的"按钮 + Popup（搜索框 + 列表）"而不是原生 <c>ComboBox</c>：原生下拉没有搜索
    /// 位置，资源一多就只能靠滚；而且这里每一项要显示缩略图 + 挂载路径两行信息，配合键盘
    /// 上下/回车/Esc 才像个能用的选择器。
    /// </para>
    /// </summary>
    public partial class PuiResourcePicker : UserControl
    {
        public static readonly DependencyProperty SelectedReferenceProperty = DependencyProperty.Register(
            nameof(SelectedReference), typeof(string), typeof(PuiResourcePicker),
            new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnSelectedReferenceChanged));

        /// <summary>正在编辑的 <c>.pui</c> 路径：用来定位所属项目、拿到那个项目的资源清单。</summary>
        public static readonly DependencyProperty SourceFilePathProperty = DependencyProperty.Register(
            nameof(SourceFilePath), typeof(string), typeof(PuiResourcePicker),
            new FrameworkPropertyMetadata(null, OnSourceFilePathChanged));

        public string SelectedReference
        {
            get => (string)GetValue(SelectedReferenceProperty);
            set => SetValue(SelectedReferenceProperty, value);
        }

        public string SourceFilePath
        {
            get => (string)GetValue(SourceFilePathProperty);
            set => SetValue(SourceFilePathProperty, value);
        }

        /// <summary>
        /// 只在用户真的在下拉里选了/清空了资源时触发（即 <see cref="Commit"/> 发起的这一次），
        /// 不包括外部把 <see cref="SelectedReference"/> 设成新值的情况（比如属性面板换了
        /// SelectedElement）——否则单纯切换选中元素就会被当成"编辑"，把文档误标记为已修改。
        /// 跟 <see cref="PuiColorPicker.HexRgbaChanged"/> 是同一套约定。
        /// </summary>
        public event EventHandler SelectedReferenceChanged;

        private readonly ObservableCollection<PolarisImageResource> _results = new ObservableCollection<PolarisImageResource>();
        private PolarisResourceCatalog _catalog;
        private bool _committing;

        public PuiResourcePicker()
        {
            InitializeComponent();
            ResultList.ItemsSource = _results;
            Loaded += (s, e) => UpdateDisplay();
        }

        private static void OnSelectedReferenceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var picker = (PuiResourcePicker)d;
            if (picker._committing)
                picker.SelectedReferenceChanged?.Invoke(picker, EventArgs.Empty);
            else
                picker.UpdateDisplay();
        }

        private static void OnSourceFilePathChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
            => ((PuiResourcePicker)d).RebindCatalog(e.NewValue as string);

        private void RebindCatalog(string puiFilePath)
        {
            if (_catalog != null)
                _catalog.Changed -= Catalog_Changed;

            _catalog = string.IsNullOrEmpty(puiFilePath) ? null : PolarisResourceCatalog.ForPuiFile(puiFilePath);

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
        /// 收起状态的显示：能在清单里找到就显示缩略图 + 短名；找不到但字段引用非空，说明字段被
        /// 改名/删了——原样显示引用并标一个 ⚠，绝不悄悄清掉用户填的值（那样用户根本不知道
        /// 自己的图片什么时候丢的）。
        /// </summary>
        private void UpdateDisplay()
        {
            string reference = SelectedReference ?? "";
            ClearButton.Visibility = reference.Length == 0 ? Visibility.Collapsed : Visibility.Visible;

            if (reference.Length == 0)
            {
                SelectedText.Text = "(none)";
                SelectedText.ToolTip = null;
                SelectedThumbBorder.Visibility = Visibility.Collapsed;
                SelectedThumb.Source = null;
                return;
            }

            if (_catalog != null && _catalog.TryGet(reference, out PolarisImageResource resource))
            {
                SelectedText.Text = resource.DisplayName;
                SelectedText.ToolTip = resource.Reference + "\n" + resource.Detail;
                SelectedThumb.Source = resource.Thumbnail;
                SelectedThumbBorder.Visibility = resource.Thumbnail == null ? Visibility.Collapsed : Visibility.Visible;
                return;
            }

            SelectedText.Text = "⚠ " + reference;
            SelectedText.ToolTip = "This resource field was not found in the project. It may have been renamed or removed.";
            SelectedThumbBorder.Visibility = Visibility.Collapsed;
            SelectedThumb.Source = null;
        }

        private void OpenButton_Click(object sender, RoutedEventArgs e) => DropDown.IsOpen = !DropDown.IsOpen;

        private void DropDown_Opened(object sender, EventArgs e)
        {
            SearchBox.Text = "";
            ApplyFilter();
            SelectCurrentInList();
            // Popup 刚 Opened 时内容还没进入焦点范围，就地 Focus() 会落空（WPF Popup 的老毛病），
            // 推到下一拍输入优先级再抢一次——否则打开下拉后得先点一下搜索框才能打字。
            Dispatcher.BeginInvoke(new Action(() => SearchBox.Focus()), DispatcherPriority.Input);
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => ApplyFilter();

        private void ApplyFilter()
        {
            string[] terms = SplitTerms(SearchBox.Text);
            IReadOnlyList<PolarisImageResource> all = _catalog?.Images ?? new List<PolarisImageResource>();

            _results.Clear();
            foreach (PolarisImageResource resource in all)
            {
                if (resource.Matches(terms))
                    _results.Add(resource);
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
                ? "This project has no MImage resource fields. Add [PolarisResource(\"file\")] MImage fields to a class tagged with [PolarisResourceFolder(\"folder\")]."
                : "No resource matches the search.";
        }

        private static string[] SplitTerms(string search)
        {
            if (string.IsNullOrWhiteSpace(search))
                return null;
            return search.ToLowerInvariant().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        }

        private void SelectCurrentInList()
        {
            string reference = SelectedReference ?? "";
            for (int i = 0; i < _results.Count; i++)
            {
                if (string.Equals(_results[i].Reference, reference, StringComparison.Ordinal))
                {
                    ResultList.SelectedIndex = i;
                    ResultList.ScrollIntoView(_results[i]);
                    return;
                }
            }
        }

        // 鼠标点选走 PreviewMouseLeftButtonUp 而不是 SelectionChanged：后者在键盘上下键移动高亮时
        // 也会触发，那样每按一下方向键都会往文档里写一次值（还会各记一条撤销）。
        private void ResultList_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (ItemUnder(e.OriginalSource as DependencyObject) is PolarisImageResource resource)
            {
                Commit(resource.Reference);
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
            if (ResultList.SelectedItem is PolarisImageResource resource)
                Commit(resource.Reference);
        }

        private void ClearButton_Click(object sender, RoutedEventArgs e) => Commit("");

        private void Commit(string reference)
        {
            DropDown.IsOpen = false;

            // 选的就是当前那个：不写值、也不报"改过了"，免得平白多一条撤销记录。
            if (string.Equals(SelectedReference ?? "", reference, StringComparison.Ordinal))
                return;

            _committing = true;
            try
            {
                SetCurrentValue(SelectedReferenceProperty, reference);
                UpdateDisplay();
            }
            finally
            {
                _committing = false;
            }
        }
    }
}
