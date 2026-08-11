using Microsoft.VisualStudio.Shell;
using PolarisTools.Pui.PuiSolutions.ViewModel;
using PolarisTools.Pui.PuiSolutions.ViewModel.NodeTypes;
using System;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace PolarisTools.Pui.PuiSolutions
{
    [Guid("47f07a41-b2f9-4f84-b05e-13ed3464c218")]
    public class PuiSolutionWindow : ToolWindowPane
    {
        public PuiSolutionWindow() : base(null)
        {
            this.Caption = "PUI Graph"; // 工具窗口标题也可一起改
            this.Content = new PuiSolutionWindowControl(initGraph: true);
        }

        public PuiSolutionWindowControl Control => Content as PuiSolutionWindowControl;

        /// <summary>每次显示工具窗口时恢复启动覆盖层</summary>
        public void OnShown()
        {
            Control?.ShowStartOverlay();
        }

        public void LoadFile(string path)
        {
            Control?.LoadFromFile(path);
            if (!string.IsNullOrEmpty(path))
                Caption = $"PUI Graph — {System.IO.Path.GetFileName(path)}";
        }
    }
    
    /// <summary>
    /// 节点内容桥：Type → 面板。
    /// </summary>
    public class NodeTypeContentConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is null) return null!;
            return NodeTypeFactory.Get((NodeType)value).CreateContent();
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => null!;
    }

    /// <summary>
    /// 新模型下所有连接都是同一种语义（状态跳转），不再需要按 ConnectorType 取色——
    /// 固定用一种颜色，仅用"是否可删除"（Removable=false，比如某些系统保留连线）做深浅区分。
    /// </summary>
    public static class ConnectorColors
    {
        public static readonly Color Default = Color.FromRgb(0x2E, 0xCC, 0x71);

        /// <summary>向黑色靠近，factor=0.10 表示加深 10%。</summary>
        public static Color Darken(Color color, double factor)
        {
            factor = Math.Min(Math.Max(factor, 0), 1);
            double keep = 1.0 - factor;
            return Color.FromRgb(
                (byte)Math.Round(color.R * keep),
                (byte)Math.Round(color.G * keep),
                (byte)Math.Round(color.B * keep));
        }

        public static SolidColorBrush Brush(bool darken = false)
        {
            var c = darken ? Darken(Default, 0.10) : Default;
            var brush = new SolidColorBrush(c);
            brush.Freeze();
            return brush;
        }
    }

    /// <summary>节点级右键菜单用：按 NodeType 决定"刷新"项是否显示（仅 PuiState）、
    /// "删除"项是否可用（Entry/Exit 不可删除）。</summary>
    public class NodeTypeMenuConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var type = value as NodeType?;
            return parameter as string switch
            {
                "IsPuiState" => type == NodeType.PuiState ? Visibility.Visible : Visibility.Collapsed,
                "IsDeletable" => type != NodeType.Entry && type != NodeType.Exit,
                _ => Visibility.Collapsed,
            };
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    public class ConnectorBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => ConnectorColors.Brush();

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => Binding.DoNothing;
    }

    public class ConnectionStrokeConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool darken = value is ConnectionViewModel connection && !connection.Removable;
            return ConnectorColors.Brush(darken);
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => Binding.DoNothing;
    }

}
