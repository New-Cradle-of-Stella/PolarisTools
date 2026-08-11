using Polaris.PUI.Wire;
using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace PolarisTools.Pui.PuiVisualEditor.Controls
{
    /// <summary>
    /// 单行数据：Primary（单列模式下的唯一值，配对模式下的 Key）+ Secondary（配对模式下的 Desc）。
    /// 每次 Primary/Secondary 变化都直接回调 <see cref="_onChanged"/>——不用订阅/退订
    /// PropertyChanged 事件（ObservableCollection.Clear() 触发的 Reset 动作不带 OldItems，
    /// 没法在那种情况下正确退订，索性从一开始就不用事件，构造时把回调传进来最简单可靠）。
    /// </summary>
    public partial class PuiStringListRow : ObservableObject
    {
        private readonly Action _onChanged;

        [ObservableProperty]
        private string _primary = "";

        [ObservableProperty]
        private string _secondary = "";

        public PuiStringListRow(Action onChanged)
        {
            _onChanged = onChanged;
        }

        partial void OnPrimaryChanged(string value) => _onChanged?.Invoke();

        partial void OnSecondaryChanged(string value) => _onChanged?.Invoke();
    }

    /// <summary>
    /// 替换 Titles/Keys/Descs/DescKeys 这几个 `;` 分隔字符串字段的裸 TextBox：拆成一行一项的
    /// 可拖拽排序列表。<see cref="ItemsText"/> 是主列（单列模式下唯一列，配对模式下是 Key）；
    /// <see cref="SecondaryItemsText"/> 只在 <see cref="IsPaired"/>=true 时使用（比如 Checks/Radio
    /// 的 Keys+Descs 是按下标一一对应的两个数组，必须一起排序，不能分开编辑）。
    /// </summary>
    public partial class PuiStringListEditor : UserControl
    {
        public static readonly DependencyProperty ItemsTextProperty = DependencyProperty.Register(
            nameof(ItemsText), typeof(string), typeof(PuiStringListEditor),
            new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnTextPropertyChanged));

        public static readonly DependencyProperty SecondaryItemsTextProperty = DependencyProperty.Register(
            nameof(SecondaryItemsText), typeof(string), typeof(PuiStringListEditor),
            new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnTextPropertyChanged));

        public static readonly DependencyProperty IsPairedProperty = DependencyProperty.Register(
            nameof(IsPaired), typeof(bool), typeof(PuiStringListEditor), new PropertyMetadata(false));

        public static readonly DependencyProperty PrimaryCaptionProperty = DependencyProperty.Register(
            nameof(PrimaryCaption), typeof(string), typeof(PuiStringListEditor), new PropertyMetadata("Key"));

        public static readonly DependencyProperty SecondaryCaptionProperty = DependencyProperty.Register(
            nameof(SecondaryCaption), typeof(string), typeof(PuiStringListEditor), new PropertyMetadata("Desc"));

        public string ItemsText
        {
            get => (string)GetValue(ItemsTextProperty);
            set => SetValue(ItemsTextProperty, value);
        }

        public string SecondaryItemsText
        {
            get => (string)GetValue(SecondaryItemsTextProperty);
            set => SetValue(SecondaryItemsTextProperty, value);
        }

        public bool IsPaired
        {
            get => (bool)GetValue(IsPairedProperty);
            set => SetValue(IsPairedProperty, value);
        }

        public string PrimaryCaption
        {
            get => (string)GetValue(PrimaryCaptionProperty);
            set => SetValue(PrimaryCaptionProperty, value);
        }

        public string SecondaryCaption
        {
            get => (string)GetValue(SecondaryCaptionProperty);
            set => SetValue(SecondaryCaptionProperty, value);
        }

        /// <summary>只在用户真的增删/编辑/拖拽重排了某一行时触发，外部整体重灌列表（比如切换了
        /// SelectedElement）不算——跟 PuiNumberBox.ValueChanged / PuiColorPicker.HexRgbaChanged
        /// 是同一个原则，避免单纯切换选中就把文档误标记为已修改。</summary>
        public event EventHandler ListChanged;

        private readonly ObservableCollection<PuiStringListRow> _rows = new ObservableCollection<PuiStringListRow>();
        private bool _isPushingText;
        private bool _isSyncingFromText;

        public PuiStringListEditor()
        {
            InitializeComponent();
            _rows.CollectionChanged += Rows_CollectionChanged;
            RowsListBox.ItemsSource = _rows;
            Loaded += (s, e) =>
            {
                RowsListBox.ItemTemplate = (DataTemplate)Resources[IsPaired ? "PairedRowTemplate" : "SingleRowTemplate"];
                RebuildRowsFromText();
            };
        }

        private static string[] SplitOrEmpty(string s) => string.IsNullOrEmpty(s) ? Array.Empty<string>() : s.Split(';');

        private void RebuildRowsFromText()
        {
            _isSyncingFromText = true;
            try
            {
                var primaryParts = SplitOrEmpty(ItemsText);
                var secondaryParts = IsPaired ? SplitOrEmpty(SecondaryItemsText) : Array.Empty<string>();
                int count = Math.Max(1, Math.Max(primaryParts.Length, secondaryParts.Length));

                _rows.Clear();
                for (int i = 0; i < count; i++)
                {
                    _rows.Add(new PuiStringListRow(OnRowChanged)
                    {
                        Primary = i < primaryParts.Length ? primaryParts[i] : "",
                        Secondary = i < secondaryParts.Length ? secondaryParts[i] : "",
                    });
                }
            }
            finally
            {
                _isSyncingFromText = false;
            }
        }

        private void OnRowChanged() => SerializeAndNotify();

        private void Rows_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e) => SerializeAndNotify();

        private void SerializeAndNotify()
        {
            if (_isSyncingFromText) return;

            _isPushingText = true;
            try
            {
                SetCurrentValue(ItemsTextProperty, string.Join(";", _rows.Select(r => r.Primary ?? "")));
                if (IsPaired)
                    SetCurrentValue(SecondaryItemsTextProperty, string.Join(";", _rows.Select(r => r.Secondary ?? "")));
            }
            finally
            {
                _isPushingText = false;
            }

            ListChanged?.Invoke(this, EventArgs.Empty);
        }

        private static void OnTextPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var editor = (PuiStringListEditor)d;
            if (!editor._isPushingText && editor.IsLoaded)
                editor.RebuildRowsFromText();
        }

        private void AddRow_Click(object sender, RoutedEventArgs e) => _rows.Add(new PuiStringListRow(OnRowChanged));

        private void RemoveRow_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is PuiStringListRow row)
                _rows.Remove(row);
        }
    }
}
