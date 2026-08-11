using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace PolarisTools.Pui.PuiVisualEditor.Controls
{
    /// <summary>
    /// 数值输入控件：替换属性面板里直接绑到 double/int 字段的裸 TextBox。
    /// 根治原来的问题——TextBox.Text 直接双向绑定数值属性、UpdateSourceTrigger=PropertyChanged 时，
    /// 打完整数部分刚输入的小数点会在同一帧里被"属性变了 -> 绑定把 Text 格式化回不带小数点的字符串"
    /// 吃掉。这里改成：输入过程中只要文本能被"宽松解析"（允许如 "12." 这种尚未打完的中间态）就把
    /// Value 推出去（保持实时预览），但 Value 变化时只有在 EditBox 没有焦点的情况下才会反过来
    /// 重新格式化 Text——正在编辑时绝不会覆盖用户刚打的字符，小数点也就不会再被吃掉。
    /// </summary>
    public partial class PuiNumberBox : UserControl
    {
        public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
            nameof(Value), typeof(double), typeof(PuiNumberBox),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnValueChanged, CoerceValue));

        public static readonly DependencyProperty MinProperty = DependencyProperty.Register(
            nameof(Min), typeof(double), typeof(PuiNumberBox), new PropertyMetadata(double.MinValue, OnRangeChanged));

        public static readonly DependencyProperty MaxProperty = DependencyProperty.Register(
            nameof(Max), typeof(double), typeof(PuiNumberBox), new PropertyMetadata(double.MaxValue, OnRangeChanged));

        public static readonly DependencyProperty StepProperty = DependencyProperty.Register(
            nameof(Step), typeof(double), typeof(PuiNumberBox), new PropertyMetadata(1.0));

        public static readonly DependencyProperty IsIntegerProperty = DependencyProperty.Register(
            nameof(IsInteger), typeof(bool), typeof(PuiNumberBox), new PropertyMetadata(false, OnRangeChanged));

        public double Value
        {
            get => (double)GetValue(ValueProperty);
            set => SetValue(ValueProperty, value);
        }

        public double Min
        {
            get => (double)GetValue(MinProperty);
            set => SetValue(MinProperty, value);
        }

        public double Max
        {
            get => (double)GetValue(MaxProperty);
            set => SetValue(MaxProperty, value);
        }

        public double Step
        {
            get => (double)GetValue(StepProperty);
            set => SetValue(StepProperty, value);
        }

        public bool IsInteger
        {
            get => (bool)GetValue(IsIntegerProperty);
            set => SetValue(IsIntegerProperty, value);
        }

        /// <summary>
        /// 只在用户真的编辑了这个控件时触发（打字/回车/上下键/滚轮/stepper），不包括外部直接把
        /// Value 设成新值的情况（比如属性面板切换了 SelectedElement，绑定把这里重新赋值）——
        /// 否则单纯切换选中元素就会被当成"编辑"，连带把整个文档误标记为已修改。
        /// </summary>
        public event EventHandler ValueChanged;

        private bool _isUserEdit;

        public PuiNumberBox()
        {
            InitializeComponent();
            Loaded += (s, e) => EditBox.Text = FormatValue(Value);
        }

        private static object CoerceValue(DependencyObject d, object baseValue)
        {
            var box = (PuiNumberBox)d;
            double v = (double)baseValue;
            if (box.IsInteger)
                v = Math.Round(v, MidpointRounding.AwayFromZero);
            if (v < box.Min) v = box.Min;
            if (v > box.Max) v = box.Max;
            return v;
        }

        private static void OnRangeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
            => d.CoerceValue(ValueProperty);

        private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var box = (PuiNumberBox)d;
            if (box.EditBox != null && !box.EditBox.IsKeyboardFocused)
                box.EditBox.Text = box.FormatValue((double)e.NewValue);
            if (box._isUserEdit)
                box.ValueChanged?.Invoke(box, EventArgs.Empty);
        }

        // EditBox_TextChanged / Nudge / CommitAndReformat 是仅有的三个"用户真的动了这个控件"的
        // 入口，统一从这里推 Value，让 OnValueChanged 能分清这次改动是不是该对外通知。
        private void SetUserValue(double v)
        {
            _isUserEdit = true;
            try
            {
                SetCurrentValue(ValueProperty, v);
            }
            finally
            {
                _isUserEdit = false;
            }
        }

        private string FormatValue(double v)
        {
            return IsInteger
                ? ((long)v).ToString(CultureInfo.InvariantCulture)
                : v.ToString("0.####", CultureInfo.InvariantCulture);
        }

        // 允许 "12." "-" "-." 这类尚未打完的中间态先原样留在文本框里，不当成解析失败去清空/报错，
        // 只是暂时不推 Value——真正推送/校验发生在下一次多输入一位数字、或 LostFocus/Enter 提交时。
        private static bool TryParseLenient(string text, out double value)
        {
            value = 0;
            if (string.IsNullOrWhiteSpace(text)) return false;
            string t = text.Trim();
            if (t == "-" || t == "." || t == "-.") return false;
            if (t.EndsWith(".", StringComparison.Ordinal))
                t = t.Substring(0, t.Length - 1);
            return double.TryParse(t, NumberStyles.Float | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value);
        }

        private void EditBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (TryParseLenient(EditBox.Text, out double v))
                SetUserValue(v);
        }

        private void EditBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            string proposed = EditBox.Text.Remove(EditBox.SelectionStart, EditBox.SelectionLength)
                                           .Insert(EditBox.SelectionStart, e.Text);
            if (!IsValidPartialNumber(proposed))
                e.Handled = true;
        }

        private bool IsValidPartialNumber(string text)
        {
            if (text.Length == 0) return true;
            int i = 0;
            if (text[0] == '-')
            {
                if (Min >= 0) return false;
                i = 1;
            }
            bool seenDot = false;
            for (; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '.')
                {
                    if (IsInteger || seenDot) return false;
                    seenDot = true;
                }
                else if (!char.IsDigit(c))
                {
                    return false;
                }
            }
            return true;
        }

        private void EditBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Up) { Nudge(Step); e.Handled = true; }
            else if (e.Key == Key.Down) { Nudge(-Step); e.Handled = true; }
            else if (e.Key == Key.Enter) { CommitAndReformat(); e.Handled = true; }
        }

        private void EditBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) => EditBox.SelectAll();

        // Blur 时无条件重新格式化（不管 OnValueChanged 是否已经因为"当时还有焦点"跳过了这一步），
        // 保证离开输入框后文本永远跟 Value 一致——清掉多余的尾随小数点、超出 Min/Max 被夹回的值等。
        private void EditBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) => EditBox.Text = FormatValue(Value);

        private void UpButton_Click(object sender, RoutedEventArgs e) => Nudge(Step);

        private void DownButton_Click(object sender, RoutedEventArgs e) => Nudge(-Step);

        private void Nudge(double delta)
        {
            SetUserValue(Value + delta);
            EditBox.Text = FormatValue(Value);
            EditBox.CaretIndex = EditBox.Text.Length;
        }

        private void CommitAndReformat()
        {
            if (TryParseLenient(EditBox.Text, out double v))
                SetUserValue(v);
            EditBox.Text = FormatValue(Value);
            EditBox.SelectAll();
        }

        private void UserControl_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (!EditBox.IsKeyboardFocused) return;
            Nudge(e.Delta > 0 ? Step : -Step);
            e.Handled = true;
        }
    }
}
