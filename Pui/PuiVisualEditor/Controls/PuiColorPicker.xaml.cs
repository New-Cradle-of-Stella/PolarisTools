using Polaris.UI.Wire;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace PolarisTools.Pui.PuiVisualEditor.Controls
{
    /// <summary>
    /// RRGGBBAA 十六进制颜色输入控件：替换 Text.Color/BackgroundColor/BorderColor、ColorCell.DefColor
    /// 这四处直接编辑十六进制字符串的裸 TextBox。色块 + hex 文本框可以直接粘贴/看到原始值，
    /// 点色块展开的面板给 R/G/B/A 四个滑条 + 数字框，不用死记十六进制去调某一个通道。
    /// </summary>
    public partial class PuiColorPicker : UserControl
    {
        public static readonly DependencyProperty HexRgbaProperty = DependencyProperty.Register(
            nameof(HexRgba), typeof(string), typeof(PuiColorPicker),
            new FrameworkPropertyMetadata("FFFFFFFF", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnHexRgbaChanged));

        public string HexRgba
        {
            get => (string)GetValue(HexRgbaProperty);
            set => SetValue(HexRgbaProperty, value);
        }

        /// <summary>
        /// 只在用户真的通过 hex 文本框或 RGBA 通道改了颜色时触发（即 PushHex 发起的这一次），
        /// 不包括外部直接把 HexRgba 设成新值的情况（比如属性面板切换了 SelectedElement）——
        /// 否则单纯切换选中元素就会被当成"编辑"，连带把整个文档误标记为已修改。
        /// </summary>
        public event EventHandler HexRgbaChanged;

        private bool _syncingFromHex;
        private bool _updatingChannelsFromHex;

        public PuiColorPicker()
        {
            InitializeComponent();
            Loaded += (s, e) => SyncControlsFromHex(HexRgba);
        }

        private static string FormatHex(byte r, byte g, byte b, byte a) => $"{r:X2}{g:X2}{b:X2}{a:X2}";

        private static void OnHexRgbaChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var picker = (PuiColorPicker)d;
            if (picker._syncingFromHex)
                picker.HexRgbaChanged?.Invoke(picker, EventArgs.Empty);
            else
                picker.SyncControlsFromHex((string)e.NewValue);
        }

        // RRGGBBAA（跟 PuiElement.Color/BackgroundColor/BorderColor/DefColor 的字段顺序一致），
        // 不是 WPF 惯用的 AARRGGBB——解析走 PuiColor.TryParse，跟生成器/预览渲染同一份实现。
        private void SyncControlsFromHex(string hex)
        {
            if (!PuiColor.TryParse(hex, out PuiColor color))
                return;

            byte r = color.R, g = color.G, b = color.B, a = color.A;
            var brush = new SolidColorBrush(Color.FromArgb(a, r, g, b));
            SwatchPreview.Background = brush;
            PopupPreview.Background = brush;

            if (!HexBox.IsKeyboardFocused)
                HexBox.Text = hex.ToUpperInvariant();

            // 四个通道框的 Value 挨个赋值，每一下都会同步触发 ChannelBox_ValueChanged——如果不拦截，
            // 赋值 R 的那一刻 G/B/A 还是上一次的旧值，会用"只改了 R、其余三个还没来得及更新"的
            // 错误中间态拼出一个错的 hex 又推回 HexRgba（绑的可能是 PuiElement.Color 之类的真实数据）。
            // 这里只是把 hex 单向同步进四个框，不需要它们再反过来推一次 hex。
            _updatingChannelsFromHex = true;
            try
            {
                RBox.Value = r;
                GBox.Value = g;
                BBox.Value = b;
                ABox.Value = a;
            }
            finally
            {
                _updatingChannelsFromHex = false;
            }
        }

        private void SwatchButton_Click(object sender, RoutedEventArgs e) => ChannelsPopup.IsOpen = !ChannelsPopup.IsOpen;

        private void HexBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            foreach (char c in e.Text)
            {
                bool isHex = (c >= '0' && c <= '9') || (c >= 'A' && c <= 'F') || (c >= 'a' && c <= 'f');
                if (!isHex) { e.Handled = true; return; }
            }
        }

        private void HexBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (HexBox.Text.Length == 8 && PuiColor.TryParse(HexBox.Text, out _))
                PushHex(HexBox.Text.ToUpperInvariant());
        }

        private void HexBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) => HexBox.Text = HexRgba.ToUpperInvariant();

        // 挂在 Slider.ValueChanged 上，不是 PuiNumberBox.ValueChanged——后者现在只在用户直接在
        // 数字框里打字/点 stepper/滚轮时才触发（这是上一轮为了不让"切换选中元素"被误判成"用户
        // 编辑"特意收紧的），拖动滑条只是通过双向绑定改了 PuiNumberBox.Value，不会经过那条
        // "用户编辑"路径，PuiNumberBox.ValueChanged 就不会响，导致拖滑条时 hex/预览色完全不跟着
        // 变。Slider.ValueChanged 是标准 WPF 事件，不管 Value 是拖出来的还是绑定改的都会触发，
        // 两个方向（拖滑条、在数字框里直接打字）都能覆盖到。
        private void ChannelSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_updatingChannelsFromHex) return;
            byte r = (byte)RBox.Value, g = (byte)GBox.Value, b = (byte)BBox.Value, a = (byte)ABox.Value;
            PushHex(FormatHex(r, g, b, a));
        }

        private void PushHex(string hex)
        {
            _syncingFromHex = true;
            try
            {
                SetCurrentValue(HexRgbaProperty, hex);
                SyncControlsFromHex(hex);
            }
            finally
            {
                _syncingFromHex = false;
            }
        }
    }
}
