using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Polaris.Map.Authoring;

namespace PolarisTools.Map.Editor;

public partial class PmapEditorControl
{
    private readonly Dictionary<uint, BitmapSource> _previewImages = new Dictionary<uint, BitmapSource>();
    private string? _previewDirectory;
    private Border? _selectionOverlay;

    private void RenderBlueprintFast()
    {
        double cell = BaseCell * _zoom;
        double width = Math.Max(1, _document.Width) * cell;
        double height = Math.Max(1, _document.Height) * cell;
        BlueprintCanvas.Children.Clear();
        BlueprintCanvas.Width = width;
        BlueprintCanvas.Height = height;

        Color mapColor = ToWpfColor(_document.Background, Color.FromRgb(245, 247, 249));
        BlueprintCanvas.Background = new SolidColorBrush(mapColor);
        bool darkMap = .2126 * mapColor.R + .7152 * mapColor.G + .0722 * mapColor.B < 120;
        Brush minor = new SolidColorBrush(darkMap
            ? Color.FromArgb(38, 255, 255, 255) : Color.FromArgb(54, 32, 38, 44));
        Brush major = new SolidColorBrush(darkMap
            ? Color.FromArgb(72, 255, 255, 255) : Color.FromArgb(92, 28, 34, 40));

        var drawing = new DrawingGroup();
        using (DrawingContext dc = drawing.Open())
        {
            for (int x = 0; x <= _document.Width; x++)
            {
                Brush brush = x % 5 == 0 ? major : minor;
                dc.DrawLine(new Pen(brush, x % 5 == 0 ? 1.0 : .55),
                    new Point(x * cell, 0), new Point(x * cell, height));
            }
            for (int y = 0; y <= _document.Height; y++)
            {
                Brush brush = y % 5 == 0 ? major : minor;
                dc.DrawLine(new Pen(brush, y % 5 == 0 ? 1.0 : .55),
                    new Point(0, y * cell), new Point(width, y * cell));
            }

            foreach (PmapLayer layer in _document.Layers)
                foreach (PmapElement element in layer.Elements)
                    DrawElement(dc, element, cell);
        }

        var image = new Image
        {
            Source = new DrawingImage(drawing),
            Width = width,
            Height = height,
            Stretch = Stretch.None,
            IsHitTestVisible = false,
        };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
        BlueprintCanvas.Children.Add(image);

        if (_selection is PmapElement selected)
        {
            _selectionOverlay = new Border
            {
                Width = Math.Max(8, selected.VisualWidth * cell),
                Height = Math.Max(8, selected.VisualHeight * cell),
                Background = Brushes.Transparent,
                BorderBrush = Brushes.White,
                BorderThickness = new Thickness(2),
                IsHitTestVisible = false,
            };
            Canvas.SetLeft(_selectionOverlay, selected.X * cell);
            Canvas.SetTop(_selectionOverlay, selected.Y * cell);
            Panel.SetZIndex(_selectionOverlay, 10000);
            BlueprintCanvas.Children.Add(_selectionOverlay);
        }
        else
        {
            _selectionOverlay = null;
        }

        CoordinateText.Text = $"28 px / cell · {_zoom * 100:0}% · X → · Y ↓"
            + (_previewDirectory == null ? "" : " · ORIGINALS");
    }

    private void DrawElement(DrawingContext dc, PmapElement element, double cell)
    {
        Rect rect = new Rect(element.X * cell, element.Y * cell,
            Math.Max(8, element.VisualWidth * cell), Math.Max(8, element.VisualHeight * cell));
        BitmapSource? bitmap = IsImageElement(element.Kind) ? PreviewImage(element) : null;
        if (bitmap != null)
        {
            double opacity = Math.Max(.16, element.Opacity / 100.0);
            dc.PushOpacity(opacity);
            Point center = new Point(rect.X + rect.Width / 2, rect.Y + rect.Height / 2);
            dc.PushTransform(new TranslateTransform(center.X, center.Y));
            int rotation = ((element.Rotation % 4) + 4) % 4;
            if (rotation != 0) dc.PushTransform(new RotateTransform(rotation * 90));
            if (element.Flip) dc.PushTransform(new ScaleTransform(-1, 1));
            bool sideways = rotation == 1 || rotation == 3;
            double drawWidth = sideways ? rect.Height : rect.Width;
            double drawHeight = sideways ? rect.Width : rect.Height;
            dc.DrawImage(bitmap, new Rect(-drawWidth / 2, -drawHeight / 2, drawWidth, drawHeight));
            if (element.Flip) dc.Pop();
            if (rotation != 0) dc.Pop();
            dc.Pop();
            dc.Pop();
            return;
        }

        Color color = ToWpfColor(element.Color, Color.FromRgb(91, 100, 119));
        Brush fill = new SolidColorBrush(color);
        dc.PushOpacity(Math.Max(.16, IsImageElement(element.Kind) ? element.Opacity / 100.0 : .78));
        dc.DrawRoundedRectangle(fill, new Pen(new SolidColorBrush(Color.FromArgb(185, 20, 24, 30)), 1),
            rect, element.Kind == PmapElementKind.Picture || element.Kind == PmapElementKind.SubMap ? 4 * _zoom : 1,
            element.Kind == PmapElementKind.Picture || element.Kind == PmapElementKind.SubMap ? 4 * _zoom : 1);
        dc.Pop();

        if (rect.Width >= 48 && rect.Height >= 17)
        {
            string label = !string.IsNullOrWhiteSpace(element.Label)
                ? element.Label : DefaultElementLabel(element);
            var text = new FormattedText(label, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                new Typeface("Consolas"), Math.Max(9, 11 * _zoom), BestTextColor(color), 1.0)
            {
                MaxTextWidth = Math.Max(1, rect.Width - 8),
                MaxTextHeight = Math.Max(1, rect.Height),
                Trimming = TextTrimming.CharacterEllipsis,
                TextAlignment = TextAlignment.Center,
            };
            dc.DrawText(text, new Point(rect.X + 4, rect.Y + Math.Max(0, (rect.Height - text.Height) / 2)));
        }
    }

    private void BlueprintCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        Point point = e.GetPosition(BlueprintCanvas);
        PmapElement? element = HitElement(point, BaseCell * _zoom);
        if (element == null) return;
        _selection = element;
        _dragElement = element;
        _dragStart = point;
        _dragX = element.X;
        _dragY = element.Y;
        SyncInspector();
        RenderBlueprintFast();
        BlueprintCanvas.CaptureMouse();
        e.Handled = true;
    }

    private void BlueprintCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (_dragElement == null || e.LeftButton != MouseButtonState.Pressed) return;
        double cell = BaseCell * _zoom;
        Point point = e.GetPosition(BlueprintCanvas);
        float step = _dragElement.Kind == PmapElementKind.Chip ? 1f / 28f : .25f;
        _dragElement.X = Snap(_dragX + (float)((point.X - _dragStart.X) / cell), step);
        _dragElement.Y = Snap(_dragY + (float)((point.Y - _dragStart.Y) / cell), step);
        IsDirty = true;
        SyncInspector();
        if (_selectionOverlay != null)
        {
            Canvas.SetLeft(_selectionOverlay, _dragElement.X * cell);
            Canvas.SetTop(_selectionOverlay, _dragElement.Y * cell);
        }
        CoordinateText.Text = $"x {_dragElement.X:0.##} · y {_dragElement.Y:0.##}";
    }

    private void BlueprintCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_dragElement == null) return;
        BlueprintCanvas.ReleaseMouseCapture();
        _dragElement = null;
        RenderBlueprintFast();
        e.Handled = true;
    }

    private PmapElement? HitElement(Point point, double cell)
    {
        for (int layerIndex = _document.Layers.Count - 1; layerIndex >= 0; layerIndex--)
        {
            IList<PmapElement> elements = _document.Layers[layerIndex].Elements;
            for (int elementIndex = elements.Count - 1; elementIndex >= 0; elementIndex--)
            {
                PmapElement element = elements[elementIndex];
                var rect = new Rect(element.X * cell, element.Y * cell,
                    Math.Max(8, element.VisualWidth * cell), Math.Max(8, element.VisualHeight * cell));
                if (rect.Contains(point)) return element;
            }
        }
        return null;
    }

    private uint[] PreviewImageIds()
        => _document.Layers.SelectMany(layer => layer.Elements)
            .Select(element => TryImageId(element.Image, out uint id) ? id : 0)
            .Where(id => id != 0)
            .Distinct()
            .OrderBy(id => id)
            .ToArray();

    private string LoadOriginalPreview(string extractionResult)
    {
        string[] fields = (extractionResult ?? "").Split('|');
        if (fields.Length < 2 || !Directory.Exists(fields[0]))
            throw new InvalidDataException("The game returned an invalid original-preview result.");
        _previewDirectory = fields[0];
        _previewImages.Clear();
        string missing = fields.Length > 2 && fields[2] != "0" ? " · " + fields[2] + " unavailable" : "";
        return "Original preview loaded · " + fields[1] + " unique images" + missing;
    }

    private void ClearOriginalPreview()
    {
        _previewImages.Clear();
        _previewDirectory = null;
    }

    private BitmapSource? PreviewImage(PmapElement element)
    {
        if (_previewDirectory == null || !TryImageId(element.Image, out uint id)) return null;
        if (_previewImages.TryGetValue(id, out BitmapSource cached)) return cached;
        string path = Path.Combine(_previewDirectory, id.ToString(CultureInfo.InvariantCulture) + ".png");
        if (!File.Exists(path)) return null;
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.UriSource = new Uri(path, UriKind.Absolute);
        bitmap.EndInit();
        bitmap.Freeze();
        _previewImages[id] = bitmap;
        return bitmap;
    }

    private static bool TryImageId(string source, out uint id)
    {
        id = 0;
        int hash = (source ?? "").LastIndexOf('#');
        return hash >= 0 && uint.TryParse(source.Substring(hash + 1), NumberStyles.None,
            CultureInfo.InvariantCulture, out id);
    }
}
