using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace PolarisTools.Pui.PuiVisualEditor.Controls
{
    /// <summary>
    /// Window 属性面板的"状态连接点"列表编辑器：直接绑定整个 Window PuiElement（不是像
    /// PuiStringListEditor 那样把结构化数据序列化成一段 ';' 分隔文本），因为每一行除了自身字段外，
    /// 还需要联动同一棵元素树里的兄弟节点（ButtonName 下拉框的数据源=本窗口内所有 Button 的 Name）。
    /// </summary>
    public partial class PuiStateTransitionListEditor : UserControl
    {
        public sealed class TriggerTypeOption
        {
            public PuiStateTriggerType Value { get; }
            public string Label { get; }

            public TriggerTypeOption(PuiStateTriggerType value, string label)
            {
                Value = value;
                Label = label;
            }
        }

        public static readonly DependencyProperty WindowElementProperty = DependencyProperty.Register(
            nameof(WindowElement), typeof(PuiElement), typeof(PuiStateTransitionListEditor),
            new PropertyMetadata(null, OnWindowElementChanged));

        public PuiElement WindowElement
        {
            get => (PuiElement)GetValue(WindowElementProperty);
            set => SetValue(WindowElementProperty, value);
        }

        /// <summary>只在用户真的增删/编辑了某一行时触发；外部整体重灌（比如切换 SelectedElement）不算——
        /// 跟 PuiStringListEditor.ListChanged 是同一约定，避免单纯切换选中就把文档误标记为已修改。</summary>
        public event EventHandler ListChanged;

        public IReadOnlyList<TriggerTypeOption> TriggerTypeOptions { get; } = new[]
        {
            new TriggerTypeOption(PuiStateTriggerType.ButtonClick, "按钮点击"),
            new TriggerTypeOption(PuiStateTriggerType.Cancel, "取消 / ESC"),
            new TriggerTypeOption(PuiStateTriggerType.CustomEvent, "自定义事件"),
        };

        /// <summary>当前窗口内所有 Button 元素的 Name，供 ButtonName 下拉框选择；随 Children 增删
        /// 和各 Button.Name 变化实时重建。</summary>
        public ObservableCollection<string> ButtonNames { get; } = new ObservableCollection<string>();

        public PuiStateTransitionListEditor()
        {
            InitializeComponent();
        }

        private static void OnWindowElementChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var editor = (PuiStateTransitionListEditor)d;
            editor.Unhook(e.OldValue as PuiElement);
            editor.Hook(e.NewValue as PuiElement);
            editor.RowsListBox.ItemsSource = (e.NewValue as PuiElement)?.StateTransitions;
            editor.RebuildButtonNames();
        }

        private void Hook(PuiElement window)
        {
            if (window == null) return;
            window.Children.CollectionChanged += Children_CollectionChanged;
            foreach (var child in window.Children)
                child.PropertyChanged += Child_PropertyChanged;
        }

        private void Unhook(PuiElement window)
        {
            if (window == null) return;
            window.Children.CollectionChanged -= Children_CollectionChanged;
            foreach (var child in window.Children)
                child.PropertyChanged -= Child_PropertyChanged;
        }

        private void Children_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems != null)
                foreach (PuiElement child in e.OldItems)
                    child.PropertyChanged -= Child_PropertyChanged;
            if (e.NewItems != null)
                foreach (PuiElement child in e.NewItems)
                    child.PropertyChanged += Child_PropertyChanged;
            RebuildButtonNames();
        }

        private void Child_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(PuiElement.Name))
                RebuildButtonNames();
        }

        private void RebuildButtonNames()
        {
            ButtonNames.Clear();
            if (WindowElement == null) return;
            foreach (var child in WindowElement.Children)
            {
                if (child.ElementType == PuiElementType.Button && !string.IsNullOrEmpty(child.Name))
                    ButtonNames.Add(child.Name);
            }
        }

        private void AddRow_Click(object sender, RoutedEventArgs e)
        {
            if (WindowElement == null) return;
            WindowElement.StateTransitions.Add(new PuiStateTransition());
            ListChanged?.Invoke(this, EventArgs.Empty);
        }

        private void RemoveRow_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is PuiStateTransition row)
                WindowElement?.StateTransitions.Remove(row);
            ListChanged?.Invoke(this, EventArgs.Empty);
        }

        private void Row_Changed(object sender, RoutedEventArgs e) => ListChanged?.Invoke(this, EventArgs.Empty);

        private void Row_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is ComboBox cb && !cb.IsKeyboardFocusWithin) return;
            ListChanged?.Invoke(this, EventArgs.Empty);
        }

        private void Row_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (sender is TextBox tb && !tb.IsKeyboardFocusWithin) return;
            ListChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public class PuiStateTriggerTypeToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is not PuiStateTriggerType t) return Visibility.Collapsed;
            return t.ToString() == parameter as string ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
