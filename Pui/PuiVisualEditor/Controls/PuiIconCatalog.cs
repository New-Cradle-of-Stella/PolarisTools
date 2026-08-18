using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace PolarisTools.Pui.PuiVisualEditor.Controls
{
    /// <summary>
    /// 用来替换编辑器里所有 emoji 图标（💾⚡📝🔲🔳☑🔘🎚⌨🔢🎨🖼✕☰）的极简矢量线性图标集合。
    /// 全部是 16x16 网格下的描边图形（配合 IconPathStyle 使用，Stroke 由样式统一绑到主题文字色，
    /// 这里只提供几何形状），不依赖任何本机字体的 emoji 字形——不同机器/字体下不会走样，颜色也能
    /// 跟着 VS 亮/暗主题的 Foreground 走，emoji 字符做不到这两点。
    ///
    /// 每个类型对应的图标都是命名的静态属性（不是只有一个按参数取值的方法），这样 XAML 里固定
    /// 已知类型的地方（比如属性面板里某个 SectionChip 永远对应 Text 类型）可以直接用
    /// {x:Static controls:PuiIconCatalog.Text} 取，不需要额外套一层 Binding+Converter；
    /// 只有工具箱那种"图标由运行时的 Type 值决定"的地方才需要 <see cref="ElementTypeToIconConverter"/>。
    /// </summary>
    public static class PuiIconCatalog
    {
        public static Geometry Save { get; } = Geometry.Parse("M2,2 H11 L14,5 V14 H2 Z M5,2 V6 H10 V2 M4,10 H12");
        public static Geometry HotReload { get; } = Geometry.Parse("M9,1 L3,9 L7,9 L6,15 L13,6 L9,6 Z");
        public static Geometry Trash { get; } = Geometry.Parse("M4,4 H12 M6,4 V2 H10 V4 M5,4 L6,14 H10 L11,4");
        public static Geometry Unlink { get; } = Geometry.Parse("M3,10 L7,6 M9,10 L13,6");
        public static Geometry DragHandle { get; } = Geometry.Parse("M3,5 H13 M3,8 H13 M3,11 H13");
        public static Geometry Plus { get; } = Geometry.Parse("M8,3 V13 M3,8 H13");
        public static Geometry Close { get; } = Geometry.Parse("M3,3 L13,13 M13,3 L3,13");
        public static Geometry Code { get; } = Geometry.Parse("M5,4 L2,8 L5,12 M11,4 L14,8 L11,12");
        public static Geometry Export { get; } = Geometry.Parse("M3,11 V13 H13 V11 M8,9 V2 M5,5 L8,2 L11,5");
        public static Geometry Import { get; } = Geometry.Parse("M3,11 V13 H13 V11 M8,2 V9 M5,6 L8,9 L11,6");
        public static Geometry Check { get; } = Geometry.Parse("M3,8.5 L6.5,12 L13,4");
        public static Geometry Pencil { get; } = Geometry.Parse("M3,13 L3.6,10.4 L11,3 L13,5 L5.6,12.4 Z M9.5,4.5 L11.5,6.5");

        public static Geometry Button { get; } = Geometry.Parse("M2,5 H14 V11 H2 Z");
        public static Geometry Text { get; } = Geometry.Parse("M3,4 H13 M8,4 V12");
        public static Geometry LineBreak { get; } = Geometry.Parse("M11,3 V9 H4 M6,7 L4,9 L6,11");
        public static Geometry Separator { get; } = Geometry.Parse("M2,8 H14");
        public static Geometry ButtonMulti { get; } = Geometry.Parse("M2,2 H7 V7 H2 Z M9,2 H14 V7 H9 Z M2,9 H7 V14 H2 Z M9,9 H14 V14 H9 Z");
        public static Geometry Checks { get; } = Geometry.Parse("M2,2 H12 V12 H2 Z M4,7 L7,10 L11,4");
        public static Geometry ColorCell { get; } = Geometry.Parse("M2,2 H14 V14 H2 Z M2,14 L14,2");
        public static Geometry Image { get; } = Geometry.Parse("M2,3 H14 V13 H2 Z M2,11 L6,7 L9,10 L12,7 L14,9");
        public static Geometry Custom { get; } = Geometry.Parse("M2,2 H14 V14 H2 Z M4,5 L4,11 M6,4 L6,12 M9,9 L11,7 L9,5 M11,7 H14");
        public static Geometry Fallback { get; } = Geometry.Parse("M2,2 H14 V14 H2 Z");
        public static Geometry StateFlow { get; } = Geometry.Parse("M2,8 H9 M6,4 L10,8 L6,12 M11,3 H14 V6 M11,10 H14 V13");

        public static Geometry Radio { get; } = BuildGroup(
            new EllipseGeometry(new Point(8, 8), 6, 6),
            new EllipseGeometry(new Point(8, 8), 2, 2));

        public static Geometry Slider { get; } = BuildGroup(
            new LineGeometry(new Point(2, 8), new Point(14, 8)),
            new EllipseGeometry(new Point(10, 8), 2.2, 2.2));

        public static Geometry Input { get; } = BuildGroup(
            new RectangleGeometry(new Rect(2, 4, 12, 8)),
            new LineGeometry(new Point(5, 6), new Point(5, 10)));

        public static Geometry NumCounter { get; } = BuildGroup(
            new RectangleGeometry(new Rect(2, 4, 5, 8)),
            new RectangleGeometry(new Rect(9, 4, 5, 8)));

        public static Geometry Search { get; } = BuildGroup(
            new EllipseGeometry(new Point(6.8, 6.8), 4.6, 4.6),
            new LineGeometry(new Point(10.3, 10.3), new Point(14, 14)));

        public static Geometry ForElementType(PuiElementType type)
        {
            switch (type)
            {
                case PuiElementType.Button: return Button;
                case PuiElementType.Text: return Text;
                case PuiElementType.LineBreak: return LineBreak;
                case PuiElementType.Separator: return Separator;
                case PuiElementType.ButtonMulti: return ButtonMulti;
                case PuiElementType.Checks: return Checks;
                case PuiElementType.Radio: return Radio;
                case PuiElementType.Slider: return Slider;
                case PuiElementType.Input: return Input;
                case PuiElementType.NumCounter: return NumCounter;
                case PuiElementType.ColorCell: return ColorCell;
                case PuiElementType.Image: return Image;
                case PuiElementType.Custom: return Custom;
                default: return Fallback;
            }
        }

        private static Geometry BuildGroup(params Geometry[] children)
        {
            var group = new GeometryGroup();
            foreach (var child in children)
                group.Children.Add(child);
            return group;
        }
    }

    public class ElementTypeToIconConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is PuiElementType type ? PuiIconCatalog.ForElementType(type) : null;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
