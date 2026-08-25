using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Polaris.Map.Authoring;
using IOPath = System.IO.Path;

namespace PolarisTools.Map.Editor;

public partial class PmapEditorControl : UserControl
{
    private const double BaseCell = 28.0;
    private PmapDocument _document = PmapDocument.CreateDefault();
    private string? _path;
    private object? _selection;
    private bool _syncing;
    private bool _viewReady;
    private double _zoom = 1.0;
    private PmapElement? _dragElement;
    private Point _dragStart;
    private float _dragX;
    private float _dragY;

    internal bool IsDirty { get; private set; }

    public PmapEditorControl()
    {
        InitializeComponent();
        _viewReady = true;
        BlueprintCanvas.MouseLeftButtonDown += BlueprintCanvas_MouseLeftButtonDown;
        BlueprintCanvas.MouseMove += BlueprintCanvas_MouseMove;
        BlueprintCanvas.MouseLeftButtonUp += BlueprintCanvas_MouseLeftButtonUp;
        Loaded += (_, _) => RefreshAll();
    }

    internal void LoadFile(string path)
    {
        ClearOriginalPreview();
        _path = path;
        string text = File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8) : "";
        _document = string.IsNullOrWhiteSpace(text)
            ? PmapDocument.CreateDefault(IOPath.GetFileNameWithoutExtension(path))
            : PmapDocument.Parse(text, path);
        _selection = _document.Layers.FirstOrDefault();
        IsDirty = false;
        StatusText.Text = path;
        RefreshAll();
    }

    internal void SaveFile(string? path = null)
    {
        string target = string.IsNullOrWhiteSpace(path) ? _path ?? "" : path!;
        if (string.IsNullOrWhiteSpace(target))
            throw new InvalidOperationException("The .pmap document has no file path.");

        _document.Save(target);
        _path = target;
        IsDirty = false;
        StatusText.Text = "Saved · " + target;
    }

    private void RefreshAll()
    {
        if (!_viewReady || !IsLoaded) return;
        SyncInspector();
        RebuildTree();
        RenderBlueprint();
    }

    private void RebuildTree()
    {
        MapTree.Items.Clear();
        foreach (PmapLayer layer in _document.Layers)
        {
            var layerItem = new TreeViewItem
            {
                Header = (layer.IsKeyLayer ? "◆ " : "▱ ") + layer.Name + "  (" + layer.Elements.Count + ")",
                Tag = layer,
                IsExpanded = false,
            };
            if (layer.Elements.Count != 0)
            {
                layerItem.Items.Add(new TreeViewItem
                {
                    Header = "Expand to load " + layer.Elements.Count + " items…",
                });
                layerItem.Expanded += LayerTree_Expanded;
            }
            MapTree.Items.Add(layerItem);
        }
    }

    private void LayerTree_Expanded(object sender, RoutedEventArgs e)
    {
        if (!(sender is TreeViewItem item) || !(item.Tag is PmapLayer layer)
            || item.Items.Count != 1 || !(item.Items[0] is TreeViewItem placeholder)
            || placeholder.Tag != null) return;
        item.Items.Clear();
        foreach (PmapElement element in layer.Elements)
        {
            string label = !string.IsNullOrWhiteSpace(element.Label)
                ? element.Label
                : DefaultElementLabel(element);
            item.Items.Add(new TreeViewItem
            {
                Header = ElementGlyph(element.Kind) + " " + label,
                Tag = element,
            });
        }
    }

    private void RenderBlueprint()
    {
        // XAML 会在 InitializeComponent 尚未构造到 BlueprintCanvas 时先触发顶部 ZoomBox
        // 的 SelectionChanged。初始化期只记录控件默认值，等 Loaded 后由 RefreshAll 统一首绘。
        if (!_viewReady || BlueprintCanvas == null) return;

        RenderBlueprintFast();
    }

    private void AddGridLine(double x1, double y1, double x2, double y2, Brush stroke, double thickness)
    {
        BlueprintCanvas.Children.Add(new Line
        {
            X1 = x1, Y1 = y1, X2 = x2, Y2 = y2,
            Stroke = stroke, StrokeThickness = thickness,
            IsHitTestVisible = false,
        });
    }

    private void AddElementVisual(PmapElement element, double cell)
    {
        bool selected = ReferenceEquals(_selection, element);
        Color color = ToWpfColor(element.Color,
            Color.FromRgb(91, 100, 119));
        string label = !string.IsNullOrWhiteSpace(element.Label)
            ? element.Label
            : DefaultElementLabel(element);

        var text = new TextBlock
        {
            Text = label,
            Foreground = BestTextColor(color),
            FontFamily = new FontFamily("Consolas"),
            FontSize = Math.Max(9, 11 * _zoom),
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(4, 0, 4, 0),
            IsHitTestVisible = false,
        };
        var border = new Border
        {
            Tag = element,
            Width = Math.Max(8, element.VisualWidth * cell),
            Height = Math.Max(8, element.VisualHeight * cell),
            Background = new SolidColorBrush(color),
            BorderBrush = selected ? Brushes.White : new SolidColorBrush(Color.FromArgb(185, 20, 24, 30)),
            BorderThickness = new Thickness(selected ? 2 : 1),
            CornerRadius = element.Kind == PmapElementKind.Picture || element.Kind == PmapElementKind.SubMap
                ? new CornerRadius(4 * _zoom) : new CornerRadius(1),
            Opacity = Math.Max(.16, IsImageElement(element.Kind) ? element.Opacity / 100.0 : 0.78),
            Child = text,
            ToolTip = ElementKindName(element.Kind) + " · " + RuntimeKey(element)
                + "\n(" + F(element.X) + ", " + F(element.Y) + ")",
            Cursor = Cursors.SizeAll,
        };
        border.MouseLeftButtonDown += Element_MouseLeftButtonDown;
        border.MouseMove += Element_MouseMove;
        border.MouseLeftButtonUp += Element_MouseLeftButtonUp;
        Canvas.SetLeft(border, element.X * cell);
        Canvas.SetTop(border, element.Y * cell);
        Panel.SetZIndex(border, selected ? 10000 : BlueprintCanvas.Children.Count);
        BlueprintCanvas.Children.Add(border);
    }

    private static Brush BestTextColor(Color color)
    {
        double luminance = .2126 * color.R + .7152 * color.G + .0722 * color.B;
        return luminance > 150 ? new SolidColorBrush(Color.FromRgb(25, 29, 34)) : Brushes.White;
    }

    private void Element_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!(sender is Border border) || !(border.Tag is PmapElement element)) return;
        _selection = element;
        _dragElement = element;
        _dragStart = e.GetPosition(BlueprintCanvas);
        _dragX = element.X;
        _dragY = element.Y;
        border.CaptureMouse();
        SyncInspector();
        RebuildTree();
        border.BorderBrush = Brushes.White;
        border.BorderThickness = new Thickness(2);
        Panel.SetZIndex(border, 10000);
        e.Handled = true;
    }

    private void Element_MouseMove(object sender, MouseEventArgs e)
    {
        if (_dragElement == null || e.LeftButton != MouseButtonState.Pressed) return;
        double cell = BaseCell * _zoom;
        Point point = e.GetPosition(BlueprintCanvas);
        // 原版 TMAP 的 CP 允许像素级偏移；1/28 格正好对应一个原始像素。
        float step = _dragElement.Kind == PmapElementKind.Chip ? 1f / 28f : .25f;
        _dragElement.X = Snap(_dragX + (float)((point.X - _dragStart.X) / cell), step);
        _dragElement.Y = Snap(_dragY + (float)((point.Y - _dragStart.Y) / cell), step);
        // 原版装饰芯片可以跨出地图边界，转换后仍需能够原位编辑。
        IsDirty = true;
        SyncInspector();
        if (sender is Border border)
        {
            Canvas.SetLeft(border, _dragElement.X * cell);
            Canvas.SetTop(border, _dragElement.Y * cell);
            CoordinateText.Text = $"x {_dragElement.X:0.##} · y {_dragElement.Y:0.##}";
        }
    }

    private void Element_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border border) border.ReleaseMouseCapture();
        _dragElement = null;
        RenderBlueprint();
        e.Handled = true;
    }

    private void SyncInspector()
    {
        _syncing = true;
        try
        {
            MapKeyBox.Text = _document.Key;
            MapWidthBox.Text = _document.Width.ToString(CultureInfo.InvariantCulture);
            MapHeightBox.Text = _document.Height.ToString(CultureInfo.InvariantCulture);
            MapBackgroundBox.Text = _document.Background;
            MapCommentBox.Text = _document.Comment;
            MapCspExpectedBox.Text = _document.CspExpectedCount.ToString(CultureInfo.InvariantCulture);
            MapCspKeysBox.Text = string.Join(Environment.NewLine, _document.CspKeys);
            MapAdditionalBox.Text = string.Join(Environment.NewLine, _document.EditorAdditional);
            MapMeshRectsBox.Text = FormatMeshRects(_document.MeshRects);

            LayerInspector.Visibility = _selection is PmapLayer ? Visibility.Visible : Visibility.Collapsed;
            ElementInspector.Visibility = _selection is PmapElement ? Visibility.Visible : Visibility.Collapsed;
            if (_selection is PmapLayer layer)
            {
                LayerNameBox.Text = layer.Name;
                LayerKeyBox.IsChecked = layer.IsKeyLayer;
                LayerColorBox.Text = layer.Color;
                LayerCommentBox.Text = layer.Comment;
            }
            else if (_selection is PmapElement element)
            {
                ElementTitle.Text = ElementKindName(element.Kind);
                ElementIdBox.Text = element.Id;
                ElementImageBox.Text = element.Image;
                ElementPatternBox.Text = element.PatternId.ToString(CultureInfo.InvariantCulture);
                ElementLabelBox.Text = element.Label;
                ElementColorBox.Text = element.Color;
                ElementXBox.Text = F(element.X);
                ElementYBox.Text = F(element.Y);
                ElementWidthBox.Text = F(element.Width);
                ElementHeightBox.Text = F(element.Height);
                ElementVisualWidthBox.Text = F(element.VisualWidth);
                ElementVisualHeightBox.Text = F(element.VisualHeight);
                ElementRotationBox.Text = element.Rotation.ToString(CultureInfo.InvariantCulture);
                ElementOpacityBox.Text = element.Opacity.ToString(CultureInfo.InvariantCulture);
                ElementFlipBox.IsChecked = element.Flip;
                ElementKeyBox.Text = element.Key;
                ElementFocusXBox.Text = F(element.FocusX);
                ElementFocusYBox.Text = F(element.FocusY);
                ElementCommandBox.Text = element.Command;
                ElementCommentBox.Text = element.Comment;
                GradationKeyBox.Text = element.Key;
                GradationOrderBox.Text = element.Order.ToString(CultureInfo.InvariantCulture);
                GradationDirectionBox.Text = element.Direction.ToString(CultureInfo.InvariantCulture);
                GradationStartColorBox.Text = element.StartColor;
                GradationEndColorBox.Text = element.EndColor;
                SlicerColumnsBox.Text = element.SlicerColumns.ToString(CultureInfo.InvariantCulture);
                SlicerRowsBox.Text = element.SlicerRows.ToString(CultureInfo.InvariantCulture);
                SlicerInternalXBox.Text = FloatList(element.InternalX);
                SlicerInternalYBox.Text = FloatList(element.InternalY);
                SlicerLevelsBox.Text = FloatList(element.Levels);
                SubMapTargetBox.Text = element.TargetMap;
                SubMapBaseBox.Text = Pair(element.BaseX, element.BaseY);
                SubMapScaleBox.Text = Pair(element.ScaleX, element.ScaleY);
                SubMapScrollBox.Text = Pair(element.ScrollX, element.ScrollY);
                SubMapOrderRepeatBox.Text = element.Order + "," + element.RepeatX + "," + element.RepeatY;
                SubMapIntervalBox.Text = Pair(element.IntervalX, element.IntervalY);
                SubMapCameraBox.Text = F(element.CameraLength);
                JointThicknessBox.Text = element.Thickness.ToString(CultureInfo.InvariantCulture);
                JointPointsBox.Text = string.Join(Environment.NewLine,
                    element.Points.Select(point => Pair(point.X, point.Y)
                        + (string.IsNullOrEmpty(point.ChipId) ? "" : "," + point.ChipId)));

                bool image = IsImageElement(element.Kind);
                ImageElementInspector.Visibility = image ? Visibility.Visible : Visibility.Collapsed;
                RectElementInspector.Visibility = element.Kind == PmapElementKind.LabelPoint
                    || element.Kind == PmapElementKind.Gradation ? Visibility.Visible : Visibility.Collapsed;
                LabelPointInspector.Visibility = element.Kind == PmapElementKind.LabelPoint ? Visibility.Visible : Visibility.Collapsed;
                GradationInspector.Visibility = element.Kind == PmapElementKind.Gradation ? Visibility.Visible : Visibility.Collapsed;
                SubMapInspector.Visibility = element.Kind == PmapElementKind.SubMap ? Visibility.Visible : Visibility.Collapsed;
                JointInspector.Visibility = element.Kind == PmapElementKind.Joint ? Visibility.Visible : Visibility.Collapsed;
            }
        }
        finally { _syncing = false; }
    }

    private void Tree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is TreeViewItem item && item.Tag != null)
        {
            _selection = item.Tag;
            SyncInspector();
            RenderBlueprint();
        }
    }

    private void AddLayer_Click(object sender, RoutedEventArgs e)
    {
        var layer = new PmapLayer { Name = UniqueLayerName(), Color = "#7F7F7F" };
        if (_document.Layers.Count == 0) layer.IsKeyLayer = true;
        _document.Layers.Add(layer);
        _selection = layer;
        Changed(true);
    }

    private void AddChip_Click(object sender, RoutedEventArgs e) => AddElement(PmapElementKind.Chip);
    private void AddPicture_Click(object sender, RoutedEventArgs e) => AddElement(PmapElementKind.Picture);
    private void AddLabelPoint_Click(object sender, RoutedEventArgs e) => AddElement(PmapElementKind.LabelPoint);
    private void AddGradation_Click(object sender, RoutedEventArgs e) => AddElement(PmapElementKind.Gradation);
    private void AddSubMap_Click(object sender, RoutedEventArgs e) => AddElement(PmapElementKind.SubMap);
    private void AddJoint_Click(object sender, RoutedEventArgs e) => AddElement(PmapElementKind.Joint);

    private void AddElement(PmapElementKind kind)
    {
        PmapLayer layer = SelectedLayer() ?? _document.Layers.FirstOrDefault();
        if (layer == null)
        {
            layer = new PmapLayer { Name = "main", IsKeyLayer = true };
            _document.Layers.Add(layer);
        }
        var element = new PmapElement
        {
            Kind = kind,
            Id = UniqueElementId(ElementPrefix(kind)),
            Image = IsImageElement(kind) ? "replace/me.png" : "",
            X = 1,
            Y = 1,
            Width = 2,
            Height = 1,
            VisualWidth = kind == PmapElementKind.Chip ? 1 : 2,
            VisualHeight = 1,
            Color = DefaultElementColor(kind),
            Label = ElementKindName(kind),
            Key = kind == PmapElementKind.LabelPoint ? "Event" : kind == PmapElementKind.Gradation ? "gradation" : "",
            TargetMap = kind == PmapElementKind.SubMap ? "replace_map_key" : "",
            Order = kind == PmapElementKind.Gradation ? 5 : kind == PmapElementKind.SubMap ? 2 : 0,
        };
        if (kind == PmapElementKind.Joint)
        {
            element.Points.Add(new PmapJointPoint { X = 0, Y = 0 });
            element.Points.Add(new PmapJointPoint { X = 1, Y = 0 });
        }
        layer.Elements.Add(element);
        _selection = element;
        Changed(true);
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (_selection is PmapElement element)
        {
            PmapLayer? layer = FindLayer(element);
            layer?.Elements.Remove(element);
            _selection = layer;
        }
        else if (_selection is PmapLayer layer && _document.Layers.Count > 1)
        {
            bool wasKey = layer.IsKeyLayer;
            _document.Layers.Remove(layer);
            if (wasKey) _document.Layers[0].IsKeyLayer = true;
            _selection = _document.Layers[0];
        }
        Changed(true);
    }

    private void MapInspector_Changed(object sender, TextChangedEventArgs e)
    {
        if (!_viewReady || _syncing) return;
        _document.Key = MapKeyBox.Text.Trim();
        if (int.TryParse(MapWidthBox.Text, out int width) && width > 0) _document.Width = width;
        if (int.TryParse(MapHeightBox.Text, out int height) && height > 0) _document.Height = height;
        _document.Background = MapBackgroundBox.Text.Trim();
        _document.Comment = MapCommentBox.Text;
        if (int.TryParse(MapCspExpectedBox.Text, out int expected)) _document.CspExpectedCount = expected;
        ReplaceLines(_document.CspKeys, MapCspKeysBox.Text);
        ReplaceLines(_document.EditorAdditional, MapAdditionalBox.Text);
        TryReplaceMeshRects(_document.MeshRects, MapMeshRectsBox.Text);
        Changed(false);
    }

    private void LayerInspector_Changed(object sender, RoutedEventArgs e)
    {
        if (!_viewReady || _syncing || !(_selection is PmapLayer layer)) return;
        layer.Name = LayerNameBox.Text.Trim();
        layer.Color = LayerColorBox.Text.Trim();
        layer.Comment = LayerCommentBox.Text;
        if (LayerKeyBox.IsChecked == true)
        {
            foreach (PmapLayer other in _document.Layers) other.IsKeyLayer = ReferenceEquals(other, layer);
        }
        else if (layer.IsKeyLayer)
        {
            _syncing = true;
            LayerKeyBox.IsChecked = true;
            _syncing = false;
        }
        Changed(false);
    }

    private void ElementInspector_Changed(object sender, RoutedEventArgs e)
    {
        if (!_viewReady || _syncing || !(_selection is PmapElement element)) return;
        element.Id = ElementIdBox.Text.Trim();
        element.Image = ElementImageBox.Text.Trim();
        if (uint.TryParse(ElementPatternBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out uint pattern)) element.PatternId = pattern;
        element.Label = ElementLabelBox.Text;
        element.Color = ElementColorBox.Text.Trim();
        if (TryFloat(ElementXBox.Text, out float x)) element.X = x;
        if (TryFloat(ElementYBox.Text, out float y)) element.Y = y;
        if (TryFloat(ElementWidthBox.Text, out float width) && width >= 0) element.Width = width;
        if (TryFloat(ElementHeightBox.Text, out float height) && height >= 0) element.Height = height;
        if (TryFloat(ElementVisualWidthBox.Text, out float vw) && vw > 0) element.VisualWidth = vw;
        if (TryFloat(ElementVisualHeightBox.Text, out float vh) && vh > 0) element.VisualHeight = vh;
        if (int.TryParse(ElementRotationBox.Text, out int rotation)) element.Rotation = rotation;
        if (int.TryParse(ElementOpacityBox.Text, out int opacity)) element.Opacity = Math.Max(0, Math.Min(100, opacity));
        element.Flip = ElementFlipBox.IsChecked == true;
        if (element.Kind == PmapElementKind.LabelPoint)
        {
            element.Key = ElementKeyBox.Text.Trim();
            if (TryFloat(ElementFocusXBox.Text, out float focusX)) element.FocusX = focusX;
            if (TryFloat(ElementFocusYBox.Text, out float focusY)) element.FocusY = focusY;
            element.Command = ElementCommandBox.Text;
            element.Comment = ElementCommentBox.Text;
        }
        else if (element.Kind == PmapElementKind.Gradation)
        {
            element.Key = GradationKeyBox.Text.Trim();
            if (int.TryParse(GradationOrderBox.Text, out int order)) element.Order = order;
            if (int.TryParse(GradationDirectionBox.Text, out int direction)) element.Direction = direction;
            element.StartColor = GradationStartColorBox.Text.Trim();
            element.EndColor = GradationEndColorBox.Text.Trim();
            if (int.TryParse(SlicerColumnsBox.Text, out int columns)) element.SlicerColumns = columns;
            if (int.TryParse(SlicerRowsBox.Text, out int rows)) element.SlicerRows = rows;
            TryReplaceFloatList(element.InternalX, SlicerInternalXBox.Text);
            TryReplaceFloatList(element.InternalY, SlicerInternalYBox.Text);
            TryReplaceFloatList(element.Levels, SlicerLevelsBox.Text);
        }
        else if (element.Kind == PmapElementKind.SubMap)
        {
            element.TargetMap = SubMapTargetBox.Text.Trim();
            if (TryPair(SubMapBaseBox.Text, out float baseX, out float baseY)) { element.BaseX = baseX; element.BaseY = baseY; }
            if (TryPair(SubMapScaleBox.Text, out float scaleX, out float scaleY)) { element.ScaleX = scaleX; element.ScaleY = scaleY; }
            if (TryPair(SubMapScrollBox.Text, out float scrollX, out float scrollY)) { element.ScrollX = scrollX; element.ScrollY = scrollY; }
            if (TryInts(SubMapOrderRepeatBox.Text, 3, out int[] orderRepeat))
            { element.Order = orderRepeat[0]; element.RepeatX = orderRepeat[1]; element.RepeatY = orderRepeat[2]; }
            if (TryPair(SubMapIntervalBox.Text, out float intervalX, out float intervalY)) { element.IntervalX = intervalX; element.IntervalY = intervalY; }
            if (TryFloat(SubMapCameraBox.Text, out float camera)) element.CameraLength = camera;
        }
        else if (element.Kind == PmapElementKind.Joint)
        {
            if (int.TryParse(JointThicknessBox.Text, out int thickness)) element.Thickness = thickness;
            TryReplaceJointPoints(element.Points, JointPointsBox.Text);
        }
        Changed(false);
    }

    private void Changed(bool rebuildTree)
    {
        IsDirty = true;
        StatusText.Text = "Modified";
        if (rebuildTree) RebuildTree();
        RenderBlueprint();
        if (rebuildTree) SyncInspector();
    }

    private void Zoom_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_viewReady) return;
        if (ZoomBox?.SelectedItem is ComboBoxItem item
            && double.TryParse(item.Tag?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double zoom))
        {
            _zoom = zoom;
            RenderBlueprint();
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        try { SaveFile(); }
        catch (Exception ex) { MessageBox.Show(ex.Message, "PMap Blueprint", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    [SuppressMessage("Usage", "VSTHRD100:Avoid async void methods", Justification = "WPF routed event handler; exceptions are handled.")]
    private async void HotReload_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SaveFile();
            string xml = _document.ToXml();
            StatusText.Text = "Sending complete map…";
            (bool ok, string error) = await PmapHotReloadClient.SendAsync(_document.Key, xml, TimeSpan.FromSeconds(10));
            StatusText.Text = ok ? "Full map reload started" : error;
            if (!ok) MessageBox.Show(error, "PMap Hot Reload", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
            MessageBox.Show(ex.Message, "PMap Hot Reload", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [SuppressMessage("Usage", "VSTHRD100:Avoid async void methods", Justification = "WPF routed event handler; exceptions are handled.")]
    private async void PreviewOriginals_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            StatusText.Text = "Temporarily extracting local MapChips bundles…";
            uint[] imageIds = PreviewImageIds();
            (bool ok, string message) = await PmapHotReloadClient.RequestPreviewAsync(true, imageIds, TimeSpan.FromSeconds(95));
            if (!ok) throw new InvalidOperationException(message);
            StatusText.Text = LoadOriginalPreview(message);
            RenderBlueprint();
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
            MessageBox.Show(ex.Message, "Original Map Preview", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    [SuppressMessage("Usage", "VSTHRD100:Avoid async void methods", Justification = "WPF routed event handler; exceptions are handled.")]
    private async void ClearPreview_Click(object sender, RoutedEventArgs e)
    {
        (bool ok, string message) = await PmapHotReloadClient.RequestPreviewAsync(false, null, TimeSpan.FromSeconds(20));
        ClearOriginalPreview();
        RenderBlueprint();
        StatusText.Text = message;
        if (!ok) MessageBox.Show(message, "Original Map Preview", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private PmapLayer? SelectedLayer()
        => _selection as PmapLayer ?? (_selection is PmapElement element ? FindLayer(element) : null);

    private PmapLayer? FindLayer(PmapElement element)
        => _document.Layers.FirstOrDefault(layer => layer.Elements.Contains(element));

    private string UniqueLayerName()
    {
        int i = _document.Layers.Count + 1;
        string name;
        do name = "layer_" + i++; while (_document.Layers.Any(x => x.Name == name));
        return name;
    }

    private string UniqueElementId(string prefix)
    {
        int i = 1;
        string id;
        do id = prefix + "_" + i++; while (_document.Layers.SelectMany(x => x.Elements).Any(x => x.Id == id));
        return id;
    }

    private static Color ToWpfColor(string value, Color fallback)
    {
        try
        {
            string text = PmapDocument.NormalizeColor(value).Substring(1);
            byte r = byte.Parse(text.Substring(0, 2), NumberStyles.HexNumber);
            byte g = byte.Parse(text.Substring(2, 2), NumberStyles.HexNumber);
            byte b = byte.Parse(text.Substring(4, 2), NumberStyles.HexNumber);
            byte a = text.Length == 8 ? byte.Parse(text.Substring(6, 2), NumberStyles.HexNumber) : (byte)255;
            return Color.FromArgb(a, r, g, b);
        }
        catch { return fallback; }
    }

    private static float Snap(float value, float step) => (float)Math.Round(value / step) * step;
    private static bool TryFloat(string value, out float result)
        => float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result);
    private static string F(float value) => value.ToString("0.#########", CultureInfo.InvariantCulture);
}
