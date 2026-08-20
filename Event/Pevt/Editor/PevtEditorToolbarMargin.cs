using System;
using System.ComponentModel.Composition;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Utilities;
using PolarisTools.Pui.PuiVisualEditor.Controls;

namespace PolarisTools.Event.Pevt.Editor;

/// <summary>
/// <c>.pevt</c> 编辑器顶部的命令条。
///
/// 两个动作都是"改完立刻想做的事"，因此放在正文上方而不是菜单里：热重载把当前这一版推给正在跑的
/// 游戏，快速本地化把这份文件里的台词一次性抽成 <c>.plang</c> 键。
/// <para>
/// 用 <see cref="IWpfTextViewMargin"/> 而不是工具栏／<c>.vsct</c> 命令：命令条只在 <c>.pevt</c> 的
/// 编辑器里出现，不占用全局工具栏的位置，也不需要作者先去"视图 → 工具栏"里找它。
/// </para>
/// </summary>
internal sealed class PevtEditorToolbarMargin : Border, IWpfTextViewMargin
{
    public const string MarginName = "PevtEditorToolbar";

    private readonly PevtEditorCommands _commands;
    private readonly Button _hotReloadButton;
    private readonly Button _localizeButton;
    private readonly TextBlock _status;

    private bool _disposed;

    public PevtEditorToolbarMargin(IWpfTextView textView, ITextDocumentFactoryService documentFactory)
    {
        _commands = new PevtEditorCommands(textView, documentFactory, SetBusy, SetStatus);

        BorderThickness = new Thickness(0, 0, 0, 1);
        Padding = new Thickness(6, 3, 6, 3);
        SetResourceReference(BackgroundProperty, EnvironmentColors.CommandBarGradientBeginBrushKey);
        SetResourceReference(BorderBrushProperty, EnvironmentColors.CommandBarBorderBrushKey);

        _hotReloadButton = CreateButton(
            "Hot reload",
            "Push every .pevt in this project to the running game, using this editor's unsaved text for this file (Polaris settings → Event (PEVT) → external .pevt import must be on).",
            PuiIconCatalog.HotReload);
        _hotReloadButton.Click += OnHotReloadClick;

        _localizeButton = CreateButton(
            "Quick localization",
            "Replace every player-facing text literal in this file with an \"&key\" localization key, and write the original text into a .plang table next to it.",
            PuiIconCatalog.Text);
        _localizeButton.Click += OnLocalizeClick;

        _status = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 0, 0),
            TextTrimming = TextTrimming.CharacterEllipsis,
            Opacity = 0.8,
        };
        _status.SetResourceReference(TextBlock.ForegroundProperty, EnvironmentColors.CommandBarTextActiveBrushKey);

        var bar = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(_hotReloadButton, Dock.Left);
        DockPanel.SetDock(_localizeButton, Dock.Left);
        bar.Children.Add(_hotReloadButton);
        bar.Children.Add(_localizeButton);
        bar.Children.Add(_status);

        Child = bar;
    }

    // ---- IWpfTextViewMargin ----

    public FrameworkElement VisualElement
    {
        get
        {
            ThrowIfDisposed();
            return this;
        }
    }

    public double MarginSize
    {
        get
        {
            ThrowIfDisposed();
            return ActualHeight;
        }
    }

    public bool Enabled
    {
        get
        {
            ThrowIfDisposed();
            return true;
        }
    }

    public ITextViewMargin? GetTextViewMargin(string marginName) =>
        string.Equals(marginName, MarginName, StringComparison.Ordinal) ? this : null;

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _hotReloadButton.Click -= OnHotReloadClick;
        _localizeButton.Click -= OnLocalizeClick;
        GC.SuppressFinalize(this);
    }

    // ---- 交互 ----

    private void OnHotReloadClick(object sender, RoutedEventArgs e) => _commands.HotReload();

    private void OnLocalizeClick(object sender, RoutedEventArgs e) => _commands.QuickLocalize();

    /// <summary>动作进行中禁用两个按钮：重复点一次热重载只会让游戏侧多做一轮整批替换。</summary>
    private void SetBusy(bool busy)
    {
        _hotReloadButton.IsEnabled = !busy;
        _localizeButton.IsEnabled = !busy;
    }

    private void SetStatus(string text)
    {
        _status.Text = text ?? "";
        _status.ToolTip = string.IsNullOrEmpty(text) ? null : text;
    }

    private static Button CreateButton(string text, string tooltip, Geometry icon)
    {
        var glyph = new System.Windows.Shapes.Path
        {
            Data = icon,
            Width = 12,
            Height = 12,
            Stretch = Stretch.Uniform,
            StrokeThickness = 1.4,
            Fill = Brushes.Transparent,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 5, 0),
        };
        glyph.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, EnvironmentColors.CommandBarTextActiveBrushKey);

        var label = new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center };
        label.SetResourceReference(TextBlock.ForegroundProperty, EnvironmentColors.CommandBarTextActiveBrushKey);

        var content = new StackPanel { Orientation = Orientation.Horizontal };
        content.Children.Add(glyph);
        content.Children.Add(label);

        var button = new Button
        {
            Content = content,
            Padding = new Thickness(8, 2, 8, 2),
            Margin = new Thickness(0, 0, 6, 0),
            BorderThickness = new Thickness(1),
            Cursor = System.Windows.Input.Cursors.Hand,
            ToolTip = tooltip,
            Focusable = false,
        };
        button.SetResourceReference(BackgroundProperty, EnvironmentColors.CommandBarGradientBeginBrushKey);
        button.SetResourceReference(BorderBrushProperty, EnvironmentColors.CommandBarBorderBrushKey);

        return button;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(MarginName);
    }
}

/// <summary>
/// 把命令条挂到每个 <c>.pevt</c> 编辑器上。
/// </summary>
[Export(typeof(IWpfTextViewMarginProvider))]
[Name(PevtEditorToolbarMargin.MarginName)]
[Order(After = PredefinedMarginNames.Top)]
[MarginContainer(PredefinedMarginNames.Top)]
[ContentType(PevtContentType.Name)]
[TextViewRole(PredefinedTextViewRoles.Document)]
internal sealed class PevtEditorToolbarMarginProvider : IWpfTextViewMarginProvider
{
#pragma warning disable 649 // MEF 通过特性注入，赋值由组合容器完成。
    /// <summary>文本缓冲区 → 磁盘文件的映射。两个动作都需要知道"这个编辑器编的是哪个文件"。</summary>
    [Import]
    private ITextDocumentFactoryService? _documentFactory;
#pragma warning restore 649

    public IWpfTextViewMargin? CreateMargin(IWpfTextViewHost wpfTextViewHost, IWpfTextViewMargin marginContainer) =>
        _documentFactory == null ? null : new PevtEditorToolbarMargin(wpfTextViewHost.TextView, _documentFactory);
}
