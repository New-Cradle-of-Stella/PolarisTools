using Polaris.Localization;
using Polaris.UI;
using Polaris.UI.Wire;
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PolarisTools.Pui.PuiVisualEditor
{
    public class PuiPreviewRenderer : FrameworkElement
    {
        // 对应真实游戏里大多数窗口实际继承的 nel.UiBoxDesigner.Awake() 覆盖后的 margin_in_lr/margin_in_tb
        // （dnSpy 反编译 Assembly-CSharp.dll 取得），不是原来瞎猜的 4/28。注意这只是"多数窗口"的近似值：
        // Designer 基类默认是 28/11，RowBtnMode 模式下是 3/50，编辑器目前没有建模"这个窗口具体用哪种
        // Designer 子类/模式"这个概念，所以统一按最常见的 UiBoxDesigner 默认值处理。
        public const double ContentInsetX = 20;
        public const double ContentInsetY = 30;

        public static readonly DependencyProperty ElementProperty =
            DependencyProperty.Register("Element", typeof(PuiElement), typeof(PuiPreviewRenderer),
                new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnElementChanged));

        public static readonly DependencyProperty SelectedElementProperty =
            DependencyProperty.Register("SelectedElement", typeof(PuiElement), typeof(PuiPreviewRenderer),
                new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        // 正在编辑的 .pui 的路径（绑定 ViewModel.FilePath）。只用来定位所属项目、把它的
        // .plang 扫成"键 → 文案"表，好让画布上的 &mymod.hello 显示成真实译文而不是键名。
        public static readonly DependencyProperty SourceFilePathProperty =
            DependencyProperty.Register("SourceFilePath", typeof(string), typeof(PuiPreviewRenderer),
                new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnSourceFilePathChanged));

        public PuiElement Element
        {
            get => (PuiElement)GetValue(ElementProperty);
            set => SetValue(ElementProperty, value);
        }

        public PuiElement SelectedElement
        {
            get => (PuiElement)GetValue(SelectedElementProperty);
            set => SetValue(SelectedElementProperty, value);
        }

        public string SourceFilePath
        {
            get => (string)GetValue(SourceFilePathProperty);
            set => SetValue(SourceFilePathProperty, value);
        }

        private PlangKeyCatalog _plangCatalog;
        private PolarisResourceCatalog _resourceCatalog;

        private static void OnSourceFilePathChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
            => ((PuiPreviewRenderer)d).RebindCatalogs(e.NewValue as string);

        private void RebindCatalogs(string puiFilePath)
        {
            if (_plangCatalog != null)
                _plangCatalog.Changed -= Catalog_Changed;
            if (_resourceCatalog != null)
                _resourceCatalog.Changed -= Catalog_Changed;

            bool hasPath = !string.IsNullOrEmpty(puiFilePath);
            _plangCatalog = hasPath ? PlangKeyCatalog.ForPuiFile(puiFilePath) : null;
            _resourceCatalog = hasPath ? PolarisResourceCatalog.ForPuiFile(puiFilePath) : null;

            if (_plangCatalog != null)
                _plangCatalog.Changed += Catalog_Changed;
            if (_resourceCatalog != null)
                _resourceCatalog.Changed += Catalog_Changed;
        }

        // FileSystemWatcher 的回调跑在线程池线程上，不能直接碰 WPF 对象。
        private void Catalog_Changed(object sender, EventArgs e)
            => Dispatcher.BeginInvoke(new Action(InvalidateVisual));

        /// <summary>
        /// 预览用取值：<c>&amp;</c> 开头的键去 <c>.plang</c> 查，查到显示译文、<b>查不到原样
        /// 显示 <c>&amp;键</c></b>（键名写错时一眼能看出来，比显示空白强）；<c>&amp;&amp;</c>
        /// 开头脱转义；其余原样。
        /// <para>
        /// 判定共用 <see cref="LocalizedString"/>——跟编译期 <c>CSharpTextEmitter</c> 和
        /// 热重载期 <c>PuiText</c> 是同一份规则，不会出现"预览按一套、真机按另一套"。
        /// 差别只在取值来源：这里查项目里的 <c>.plang</c>，真机走 <c>XX.TX.Get</c>。
        /// </para>
        /// </summary>
        private string Display(string raw)
        {
            if (!LocalizedString.TryGetKey(raw, out string key))
                return LocalizedString.Unescape(raw);

            return _plangCatalog != null && _plangCatalog.TryGet(key, out string text) ? text : raw;
        }

        public event EventHandler<PuiElement> OnElementSelected;

        private static void OnElementChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var renderer = (PuiPreviewRenderer)d;
            if (e.OldValue is PuiElement oldElem)
                renderer.UnsubscribeElement(oldElem);
            if (e.NewValue is PuiElement newElem)
                renderer.SubscribeElement(newElem);
        }

        private void SubscribeElement(PuiElement elem)
        {
            elem.PropertyChanged += Element_PropertyChanged;
            elem.Children.CollectionChanged += Children_CollectionChanged;
            foreach (var child in elem.Children)
                SubscribeElement(child);
        }

        private void UnsubscribeElement(PuiElement elem)
        {
            elem.PropertyChanged -= Element_PropertyChanged;
            elem.Children.CollectionChanged -= Children_CollectionChanged;
            foreach (var child in elem.Children)
                UnsubscribeElement(child);
        }

        private void Element_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
            => InvalidateVisual();

        private void Children_CollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems != null)
                foreach (PuiElement item in e.OldItems)
                    UnsubscribeElement(item);
            if (e.NewItems != null)
                foreach (PuiElement item in e.NewItems)
                    SubscribeElement(item);
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);
            if (Element == null) return;
            RenderElement(dc, Element, 0, 0, Element.Width);
        }

        // windowWidth：所属 Window 的原始 Width，GetEffectiveSize 在拿不到 knownSize 时的兜底会用到。
        // knownSize：RenderChildrenFlowLayout 传下来的、PuiLineLayout.Compute 已经算好的实际占位尺寸——
        // 分割线的占位宽度跟着换行光标状态走，跟 elem.Width/Height 无关，也没法在这里重新算一遍，必须
        // 用 Compute() 当时算出来的那份，否则分割线会跟画出来的行位置对不上。
        private void RenderElement(DrawingContext dc, PuiElement elem, double x, double y, double windowWidth, Size? knownSize = null)
        {
            var (effWidth, effHeight) = ResolveSize(elem, windowWidth, knownSize);
            var rect = new Rect(x, y, effWidth, effHeight);
            var isSelected = elem == SelectedElement;

            if (elem.IsLineSelected)
            {
                var highlightRect = new Rect(x - 2, y - 2, effWidth + 4, effHeight + 4);
                dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(40, 255, 200, 0)), null, highlightRect);
            }

            switch (elem.ElementType)
            {
                case PuiElementType.Window:
                    RenderWindow(dc, elem, rect, isSelected, x, y);
                    break;
                case PuiElementType.LineBreak:
                    RenderLineBreak(dc, rect, isSelected);
                    break;
                case PuiElementType.Button:
                    RenderButton(dc, elem, rect, isSelected);
                    break;
                case PuiElementType.Text:
                    RenderText(dc, elem, rect, isSelected);
                    break;
                case PuiElementType.Separator:
                    RenderSeparator(dc, rect, isSelected);
                    break;
                // localizeItems 只对 ButtonMulti 为 true：它画的是 Titles（显示用标题）。
                // Checks/Radio 这里画的是 Keys（回调返回值用的标识符，不本地化）——它们真正
                // 会被本地化的 Descs 预览网格里本来就不显示，所以这两个元素在画布上看不出
                // 本地化效果，属于既有行为。
                case PuiElementType.ButtonMulti:
                    RenderGridBox(dc, elem, rect, isSelected, Brushes.CadetBlue, Brushes.LightCyan, elem.Titles, useScale: false, localizeItems: true);
                    break;
                case PuiElementType.Checks:
                    RenderGridBox(dc, elem, rect, isSelected, Brushes.DarkOliveGreen, Brushes.PaleGreen, elem.Keys, useScale: true, localizeItems: false);
                    break;
                case PuiElementType.Radio:
                    RenderGridBox(dc, elem, rect, isSelected, Brushes.MediumPurple, Brushes.Thistle, elem.Keys, useScale: true, localizeItems: false);
                    break;
                case PuiElementType.Slider:
                    RenderSlider(dc, elem, rect, isSelected);
                    break;
                // Label 在调用点就地解析，RenderLabeledBox 本身保持中立——下面 Image 那个
                // 调用点传的是 "🖼 " + elem.Name，绝不能跟着一起被当成本地化键。
                case PuiElementType.Input:
                    RenderLabeledBox(dc, elem, rect, isSelected, Brushes.DimGray, Brushes.White, Display(elem.Label));
                    break;
                case PuiElementType.NumCounter:
                    RenderNumCounter(dc, elem, rect, isSelected);
                    break;
                case PuiElementType.ColorCell:
                    RenderColorCell(dc, elem, rect, isSelected);
                    break;
                case PuiElementType.Image:
                    RenderImage(dc, elem, rect, isSelected);
                    break;
                case PuiElementType.Custom:
                {
                    // 真正的绘制内容来自后端 IPuiCustomControl，编辑器画布画不出来（不会加载/执行后端
                    // 程序集），只示意占位区域 + 标出选了哪个后端类型（未选则提示未设置）。
                    string label = string.IsNullOrEmpty(elem.BackendType)
                        ? "⚙ " + elem.Name
                        : "⚙ " + ShortReference(elem.BackendType);
                    RenderLabeledBox(dc, elem, rect, isSelected, Brushes.DarkSlateGray, Brushes.White, label);
                    break;
                }
            }
        }

        // knownSize 有值（PuiLineLayout.Compute 已经算好的实际占位尺寸）就直接用，否则退回无状态
        // 估算。绘制（RenderElement）和命中测试（HitTestElement）必须用同一份取值逻辑，不然点击
        // 判定范围会跟画出来的方框错位。
        private static (double Width, double Height) ResolveSize(PuiElement elem, double windowWidth, Size? knownSize)
            => knownSize.HasValue
                ? (knownSize.Value.Width, knownSize.Value.Height)
                : PuiLineLayout.GetEffectiveSize(elem, windowWidth);

        // WPF 的 Pen 描边是"跨骑"在几何轮廓线上画的（一半在框内、一半在框外），直接对声明宽高的 rect
        // 描边会让画出来的可视宽高比 elem.Width/Height 多出整整一个 Pen.Thickness（左右各多半个）。这里
        // 统一先把 rect 向内收缩半个描边宽度再画，保证看到的方框宽度跟属性面板里的 Width 严格一致
        // （比如 NumCounter 设 Width=100，选中态描边 2px，不收缩的话会画成 102px 宽）。
        private static void DrawBorderedRect(DrawingContext dc, Brush fill, Pen pen, Rect rect)
        {
            if (pen != null)
            {
                double half = pen.Thickness / 2;
                rect = new Rect(rect.X + half, rect.Y + half,
                    Math.Max(0, rect.Width - pen.Thickness), Math.Max(0, rect.Height - pen.Thickness));
            }
            dc.DrawRectangle(fill, pen, rect);
        }

        private static readonly Brush WindowFillBrush = new SolidColorBrush(Color.FromRgb(0xDB, 0xD5, 0xCF));

        private void RenderWindow(DrawingContext dc, PuiElement elem, Rect rect, bool isSelected, double x, double y)
        {
            // 真机窗口没有标题栏这种 chrome，之前画的那条"Window1"灰色条纯粹是编辑器自己加的
            // 识别标记，跟真实渲染效果不符，去掉；ContentInsetY 本身是真机 margin_in_tb（内容区
            // 顶部留白），跟标题栏无关，不能因为去掉标题栏就顺手改掉。
            DrawBorderedRect(dc, WindowFillBrush, new Pen(isSelected ? Brushes.Cyan : Brushes.Gray, isSelected ? 2 : 1), rect);
            RenderChildrenFlowLayout(dc, elem, x + ContentInsetX, y + ContentInsetY, elem.Width - ContentInsetX * 2, elem.Width);
        }

        private static void RenderLineBreak(DrawingContext dc, Rect rect, bool isSelected)
        {
            var pen = new Pen(isSelected ? Brushes.Cyan : Brushes.Gray, isSelected ? 2 : 1) { DashStyle = DashStyles.Dash };
            dc.DrawRectangle(Brushes.Transparent, pen, rect);
            DrawTextCentered(dc, "↵", rect, Brushes.Gray, 14);
        }

        // "normal"（默认）皮肤按 XX.ButtonSkinNormal.Fine()（dnSpy 反编译 unsafeAssem.dll 取得）复刻：
        // 按钮本体是两层圆角矩形——阴影层（右下偏移）+ 正面层（左上偏移），圆角半径按钮高的一半，
        // 因此左右两端是半圆，整体呈"胶囊/药丸"形，不是简单直角矩形。正面层左侧还有一个小菱形
        // meshicon（游戏里紧贴文字左边那个 ❖ 标记）。真实游戏里这两层颜色随 hover/pushed/checked/
        // locked 等交互状态变化（见 ButtonSkinNormal.Fine() 的分支），这里只画静态的"未交互"状态；
        // base_color 字段本身的默认字面量反编译片段里没取到，用截图观察到的米白色近似。
        private static readonly Color PillShadowColor = Color.FromArgb(0xDD, 0x89, 0x91, 0xB8);
        private static readonly Color PillFrontColor = Color.FromArgb(0xFF, 0xF5, 0xEF, 0xDD);
        private static readonly Color PillTextColor = Color.FromArgb(0xFF, 0x41, 0x44, 0x5C);

        private void RenderButton(DrawingContext dc, PuiElement elem, Rect rect, bool isSelected)
        {
            double shadowShift = Math.Max(1.0, rect.Height * 0.06);
            double radius = rect.Height / 2;

            var shadowRect = new Rect(rect.X + shadowShift, rect.Y + shadowShift, rect.Width, rect.Height);
            dc.DrawRoundedRectangle(new SolidColorBrush(PillShadowColor), null, shadowRect, radius, radius);

            var frontPen = new Pen(isSelected ? Brushes.Cyan : Brushes.DimGray, isSelected ? 2 : 1);
            dc.DrawRoundedRectangle(new SolidColorBrush(PillFrontColor), frontPen, rect, radius, radius);

            double glyphHalf = Math.Max(2.5, rect.Height * 0.16);
            DrawDiamondGlyph(dc, rect.X + radius * 0.65, rect.Y + rect.Height / 2, glyphHalf, PillTextColor);

            DrawTextCentered(dc, Display(elem.Text), new Rect(rect.X + radius * 0.5, rect.Y, rect.Width - radius * 0.5, rect.Height),
                new SolidColorBrush(PillTextColor), Math.Min(12, rect.Height * 0.45));
        }

        private static void DrawDiamondGlyph(DrawingContext dc, double centerX, double centerY, double half, Color color)
        {
            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
            {
                ctx.BeginFigure(new Point(centerX, centerY - half), true, true);
                ctx.LineTo(new Point(centerX + half, centerY), true, false);
                ctx.LineTo(new Point(centerX, centerY + half), true, false);
                ctx.LineTo(new Point(centerX - half, centerY), true, false);
            }
            geometry.Freeze();
            dc.DrawGeometry(new SolidColorBrush(color), null, geometry);
        }

        // ColorCell 按 XX.ButtonSkinColorCell.Fine() 复刻：外层白色边框方块 + 内层实际颜色的色块
        // （内缩 4px），没有花纹/圆角——这是当前唯一"皮肤颜色即数据本身"的控件，游戏里也确实这么画。
        private void RenderColorCell(DrawingContext dc, PuiElement elem, Rect rect, bool isSelected)
        {
            DrawBorderedRect(dc, Brushes.White, new Pen(isSelected ? Brushes.Cyan : Brushes.DimGray, isSelected ? 2 : 1), rect);
            var swatchColor = ParseRgbaHex(elem.DefColor, Colors.White);
            var inner = new Rect(rect.X + 2, rect.Y + 2, Math.Max(0, rect.Width - 4), Math.Max(0, rect.Height - 4));
            dc.DrawRectangle(new SolidColorBrush(swatchColor), null, inner);

            if (elem.UseText && !string.IsNullOrEmpty(elem.Text))
            {
                var luminance = (swatchColor.R * 0.299 + swatchColor.G * 0.587 + swatchColor.B * 0.114);
                var textBrush = luminance > 140 ? Brushes.Black : Brushes.White;
                DrawTextCentered(dc, Display(elem.Text), inner, textBrush, 10);
            }
        }

        // DefColor 是 "RRGGBBAA" 十六进制字符串（对应 DsnDataColorCell.def : Color32），不是 WPF 惯用的
        // AARRGGBB。解析规则跟生成器/热重载走同一份实现（PuiColor.TryParse），预览色不会跟实际生成的
        // 颜色出现分歧。
        private static Color ParseRgbaHex(string hex, Color fallback)
            => PuiColor.TryParse(hex, out PuiColor c) ? Color.FromArgb(c.A, c.R, c.G, c.B) : fallback;

        // Color/Size 直接映射预览效果：Color 是 RRGGBBAA hex（默认 "FFFFFFFF"，没设就是白色，
        // 不是"永远白色"）；Size 对应真机 DsnDataP.size，0 表示"不覆盖、用皮肤默认字号"，这里
        // 拿不到真机皮肤默认值，退一步用原来写死的 12 当占位（其余大于 0 的值都是用户显式设的
        // 字号，直接用）。
        private void RenderText(DrawingContext dc, PuiElement elem, Rect rect, bool isSelected)
        {
            dc.DrawRectangle(Brushes.Transparent, isSelected ? new Pen(Brushes.Cyan, 1) : null, rect);
            var textColor = ParseRgbaHex(elem.Color, Colors.White);
            double fontSize = elem.Size > 0 ? elem.Size : 12;
            DrawTextAligned(dc, Display(elem.Text), rect, new SolidColorBrush(textColor), fontSize, elem.Align);
        }

        // rect 是 PuiLineLayout.Compute 算出来的有效尺寸（宽=分割线独占那一整行的可用宽度，
        // 高=LineHeight+MarginBefore+MarginAfter），不能再用 elem.Width/Height——那两个字段跟布局
        // 无关（真实游戏也不序列化 Separator 的 Width/Height）。宽度不受 Ratio 影响：Ratio 在真机里
        // 只控制线条的绘制覆盖率，不影响占多少布局空间（见 PuiTreeWalker.cs 的对应注释）。
        private static void RenderSeparator(DrawingContext dc, Rect rect, bool isSelected)
        {
            dc.DrawRectangle(Brushes.Transparent, isSelected ? new Pen(Brushes.Cyan, 1) { DashStyle = DashStyles.Dash } : null, rect);
            var lineY = rect.Y + rect.Height / 2;
            dc.DrawLine(new Pen(Brushes.Gray, 1), new Point(rect.X + 4, lineY), new Point(rect.X + rect.Width - 4, lineY));
        }

        // 给"数值/图像"这一类没有内部子结构的控件用的通用预览方块：一个填充+边框的矩形，中间居中显示
        // 一行说明文字。Button 和 ColorCell 已经按反编译到的真实皮肤绘制逻辑单独实现（见 RenderButton /
        // RenderColorCell），Input/NumCounter/Image 的皮肤绘制逻辑尚未逐一验证，仍用这个示意矩形占位，
        // 只换填充色区分类型。ButtonMulti/Checks/Radio 有真实的多项网格排布（见 RenderGridBox），
        // Slider 有真实的"主滑条+setter"两段式布局（见 RenderSlider），不再用这个占位方块。
        private static void RenderLabeledBox(DrawingContext dc, PuiElement elem, Rect rect, bool isSelected, Brush fill, Brush textBrush, string label)
        {
            DrawBorderedRect(dc, fill, new Pen(isSelected ? Brushes.Cyan : Brushes.DimGray, isSelected ? 2 : 1), rect);
            var text = string.IsNullOrEmpty(label) ? elem.Name : label;
            DrawTextCentered(dc, text, rect, textBrush, 11);
        }

        /// <summary>
        /// Image：选了资源字段、而且编辑器在磁盘上找得到对应图片文件时画出真实图片；否则退回
        /// 示意方块，标注选的是哪个资源（或"未设置"）——一眼能区分"没选图"和"选了但文件没找到/
        /// 解码失败"。
        /// <para>
        /// 绘制规则跟运行时 <c>Polaris.PUI.PuiImage.Assign</c> 一致，两边必须同时改：Uv 是
        /// 0..1 的归一化值（原点左下，Unity 纹理坐标），先换算成纹理像素矩形裁出源图，再<b>等比</b>
        /// 缩放到声明的 Width×Height 之内并居中（真机 <c>FillImageBlock</c> 只有一个 <c>scale</c>
        /// 同时作用于两轴，做不到非等比拉伸），最后乘用户填的 <c>Scale</c>。
        /// </para>
        /// </summary>
        private void RenderImage(DrawingContext dc, PuiElement elem, Rect rect, bool isSelected)
        {
            BitmapSource bitmap = ResolveImageBitmap(elem.ImageResource);
            if (bitmap == null)
            {
                string label = string.IsNullOrEmpty(elem.ImageResource)
                    ? "🖼 " + elem.Name
                    : "🖼 " + ShortReference(elem.ImageResource);
                RenderLabeledBox(dc, elem, rect, isSelected, Brushes.Gray, Brushes.White, label);
                return;
            }

            BitmapSource source = CropToUv(bitmap, elem);
            double scale = elem.Scale > 0 ? elem.Scale : 1;
            double fit = Math.Min(rect.Width / source.PixelWidth, rect.Height / source.PixelHeight) * scale;
            double destW = source.PixelWidth * fit;
            double destH = source.PixelHeight * fit;
            var destRect = new Rect(
                rect.X + (rect.Width - destW) / 2,
                rect.Y + (rect.Height - destH) / 2,
                Math.Max(0, destW),
                Math.Max(0, destH));

            // Scale > 1 时真机会溢出占位框（只被窗口遮罩裁掉），这里同样不裁——裁掉反而会让
            // 画布显示得比真机"更整齐"，掩盖了溢出这件事。
            dc.DrawImage(source, destRect);

            if (isSelected)
                DrawBorderedRect(dc, null, new Pen(Brushes.Cyan, 2), rect);
        }

        /// <summary>
        /// 按归一化 Uv 裁出源图的一块。Unity 纹理坐标原点在左下、WPF 位图原点在左上，所以纵向
        /// 要翻一次。整张图（默认的 0,0,1,1）不构造 <see cref="CroppedBitmap"/>，省一次拷贝。
        /// </summary>
        private static BitmapSource CropToUv(BitmapSource bitmap, PuiElement elem)
        {
            int texW = bitmap.PixelWidth;
            int texH = bitmap.PixelHeight;
            double uvW = elem.UvW > 0 ? elem.UvW : 1;
            double uvH = elem.UvH > 0 ? elem.UvH : 1;

            int w = Clamp((int)Math.Round(uvW * texW), 1, texW);
            int h = Clamp((int)Math.Round(uvH * texH), 1, texH);
            int x = Clamp((int)Math.Round(elem.UvX * texW), 0, texW - w);
            int yFromBottom = Clamp((int)Math.Round(elem.UvY * texH), 0, texH - h);
            int y = texH - yFromBottom - h;

            if (x == 0 && y == 0 && w == texW && h == texH)
                return bitmap;

            var cropped = new CroppedBitmap(bitmap, new Int32Rect(x, y, w, h));
            cropped.Freeze();
            return cropped;
        }

        private static int Clamp(int value, int min, int max)
            => value < min ? min : (value > max ? max : value);

        private BitmapSource ResolveImageBitmap(string reference)
            => _resourceCatalog != null && _resourceCatalog.TryGet(reference, out PolarisImageResource resource)
                ? resource.FullImage
                : null;

        // 画布上的占位标签只放"类名.字段名"，命名空间前缀在这么小的方块里放不下也没信息量。
        private static string ShortReference(string reference)
        {
            string[] parts = reference.Split('.');
            return parts.Length <= 2 ? reference : parts[parts.Length - 2] + "." + parts[parts.Length - 1];
        }

        // ButtonMulti/Checks/Radio 的外框（rect）已经是 PuiLineLayout.GetGridContainerSize 按真机
        // XX.Designer.reboundCarrForBtnMulti 公式算出来的 clms×rows 网格整体尺寸，这里按同一套公式把
        // 内部每一项摆成小方块，直观显示出真机会有的多行堆叠效果（比如 Columns=1 时纵向堆叠），而不是
        // 画一个笼统的大方框——这属于排版本身，不是纯样式装饰。useScale 对应 Checks/Radio 有整体
        // Scale、ButtonMulti 没有（真机 addButtonMultiT 不支持整体缩放）。
        private void RenderGridBox(DrawingContext dc, PuiElement elem, Rect rect, bool isSelected, Brush fill, Brush textBrush, string itemsList, bool useScale, bool localizeItems)
        {
            var items = string.IsNullOrWhiteSpace(itemsList) ? new[] { elem.Name } : itemsList.Split(';');
            if (localizeItems)
            {
                for (int i = 0; i < items.Length; i++)
                    items[i] = Display(items[i]);
            }
            int itemCount = Math.Max(1, items.Length);
            int columns = elem.Columns <= 0 ? itemCount : Math.Min(elem.Columns, itemCount);
            if (columns < 1) columns = 1;

            double scale = useScale ? elem.Scale : 1;
            double itemW = elem.Width * scale;
            double itemH = elem.Height * scale;
            double marginW = elem.MarginW * scale;
            double marginH = elem.MarginH * scale;
            var pen = new Pen(isSelected ? Brushes.Cyan : Brushes.DimGray, isSelected ? 2 : 1);

            for (int i = 0; i < itemCount; i++)
            {
                int col = i % columns;
                int row = i / columns;
                var cellRect = new Rect(rect.X + col * (itemW + marginW), rect.Y + row * (itemH + marginH), itemW, itemH);
                DrawBorderedRect(dc, fill, pen, cellRect);
                DrawTextCentered(dc, items[i], cellRect, textBrush, 10);
            }
        }

        // NumCounter 的外框（rect）已经是 PuiLineLayout.GetNumCounterEffectiveSize 按真机
        // BtnContainerNumCounter.initNumCounter 公式算出来的"每位宽度×位数"总宽度，这里按同一个
        // 位数把 rect 切成一格一格紧贴排列的方块（转轮效果），格子之间没有间距——跟 RenderGridBox
        // 的网格不一样，必须用 PuiLineLayout.GetNumCounterDigitCount 同一份实现切格子，否则位数
        // 一多就会跟布局宽度对不上。
        private static void RenderNumCounter(DrawingContext dc, PuiElement elem, Rect rect, bool isSelected)
        {
            int digitCount = Math.Max(1, PuiLineLayout.GetNumCounterDigitCount(elem));
            double digitWidth = rect.Width / digitCount;
            var pen = new Pen(isSelected ? Brushes.Cyan : Brushes.DimGray, isSelected ? 2 : 1);

            for (int i = 0; i < digitCount; i++)
            {
                var cellRect = new Rect(rect.X + i * digitWidth, rect.Y, digitWidth, rect.Height);
                DrawBorderedRect(dc, Brushes.LightYellow, pen, cellRect);
                DrawTextCentered(dc, "0", cellRect, Brushes.DarkGoldenrod, Math.Min(12, rect.Height * 0.4));
            }
        }

        // Slider 的外框（rect）已经是 PuiLineLayout.GetSliderEffectiveSize 算出来的"主滑条+间距+setter"
        // 总宽度，这里按同样的两段式把主滑条和 setter 分开画，而不是一个整体方框——直观显示真机
        // addSliderCT 会在右侧多出一块数值 setter。skin 为 "invisible" 时主滑条真机会被强制收窄到 1px。
        private void RenderSlider(DrawingContext dc, PuiElement elem, Rect rect, bool isSelected)
        {
            bool invisible = string.Equals(elem.Skin, "invisible", StringComparison.Ordinal);
            double meterWidth = invisible ? 1 : elem.Width;
            var pen = new Pen(isSelected ? Brushes.Cyan : Brushes.DimGray, isSelected ? 2 : 1);

            var meterRect = new Rect(rect.X, rect.Y, meterWidth, rect.Height);
            dc.DrawRectangle(Brushes.SeaGreen, pen, meterRect);
            if (!invisible)
                DrawTextCentered(dc, Display(elem.Text), meterRect, Brushes.PaleGreen, 11);

            var setterRect = new Rect(rect.X + meterWidth + PuiLineLayout.ItemSpacingX, rect.Y,
                Math.Max(0, elem.SetterWidth), rect.Height);
            dc.DrawRectangle(Brushes.DarkSeaGreen, pen, setterRect);
            DrawTextCentered(dc, elem.Def.ToString(System.Globalization.CultureInfo.InvariantCulture), setterRect, Brushes.White, 10);
        }

        private void RenderChildrenFlowLayout(DrawingContext dc, PuiElement parent, double startX, double startY, double maxWidth, double windowWidth)
        {
            foreach (var line in PuiLineLayout.Compute(parent, startX, startY, maxWidth, windowWidth))
                foreach (var child in line.Elements)
                {
                    var pos = line.Positions[child];
                    Size? size = line.Sizes.TryGetValue(child, out var s) ? s : (Size?)null;
                    RenderElement(dc, child, pos.X, pos.Y, windowWidth, size);
                }
        }

        // 位置和尺寸要一起传下去：HitTestElement 递归判断子元素命中范围时，分割线之类的尺寸没法脱离
        // 当时的换行光标状态重新算出来，必须用 Compute() 当次算好的那份（跟 RenderElement 同理）。
        private static Dictionary<PuiElement, (Point Position, Size Size)> CalculateChildPositions(PuiElement parent, double startX, double startY, double maxWidth, double windowWidth)
        {
            var result = new Dictionary<PuiElement, (Point, Size)>();
            foreach (var line in PuiLineLayout.Compute(parent, startX, startY, maxWidth, windowWidth))
                foreach (var child in line.Elements)
                {
                    var size = line.Sizes.TryGetValue(child, out var s) ? s : default;
                    result[child] = (line.Positions[child], size);
                }
            return result;
        }

        public static List<PuiLineInfo> ComputeWindowLines(PuiElement window)
        {
            if (window == null || window.ElementType != PuiElementType.Window)
                return new List<PuiLineInfo>();
            return PuiLineLayout.Compute(window, ContentInsetX, ContentInsetY, window.Width - ContentInsetX * 2, window.Width);
        }

        private static void DrawTextCentered(DrawingContext dc, string text, Rect rect, Brush brush, double size)
        {
            var ft = new FormattedText(text ?? "", System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight, new Typeface("Segoe UI"), size, brush, 1.0);
            dc.DrawText(ft, new Point(rect.X + (rect.Width - ft.Width) / 2, rect.Y + (rect.Height - ft.Height) / 2));
        }

        // Text 元素自己的 Align（对应真机 XX.ALIGN）跟"这一行在窗口里怎么摆"的 PuiLineAlign 是两个
        //独立的概念：这个决定文字在 Text 自身 Width×Height 范围内怎么排，之前一直没实现，永远按
        // DrawText 画在左上角，跟 Align=Center/Right 的效果对不上。Auto 真机是单独一套自动判定
        // 逻辑（未反编译验证具体规则），这里按 Left 近似处理。
        private static void DrawTextAligned(DrawingContext dc, string text, Rect rect, Brush brush, double size, PuiTextAlign align)
        {
            var ft = new FormattedText(text ?? "", System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight, new Typeface("Segoe UI"), size, brush, 1.0);
            double tx = align switch
            {
                PuiTextAlign.Center => rect.X + (rect.Width - ft.Width) / 2,
                PuiTextAlign.Right => rect.X + rect.Width - ft.Width,
                _ => rect.X,
            };
            dc.DrawText(ft, new Point(tx, rect.Y + (rect.Height - ft.Height) / 2));
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            var pos = e.GetPosition(this);
            var hit = HitTestElement(Element, pos, 0, 0, Element?.Width ?? 0);
            OnElementSelected?.Invoke(this, hit ?? Element);
        }

        private PuiElement HitTestElement(PuiElement elem, Point pos, double x, double y, double windowWidth, Size? knownSize = null)
        {
            if (elem == null) return null;
            var (effWidth, effHeight) = ResolveSize(elem, windowWidth, knownSize);
            var rect = new Rect(x, y, effWidth, effHeight);
            if (!rect.Contains(pos)) return null;

            if (elem.ElementType == PuiElementType.Window)
            {
                var childInfo = CalculateChildPositions(elem, x + ContentInsetX, y + ContentInsetY, elem.Width - ContentInsetX * 2, elem.Width);
                for (int i = elem.Children.Count - 1; i >= 0; i--)
                {
                    var child = elem.Children[i];
                    if (!childInfo.TryGetValue(child, out var info)) continue;
                    var hit = HitTestElement(child, pos, info.Position.X, info.Position.Y, elem.Width, info.Size);
                    if (hit != null) return hit;
                }
            }
            return elem;
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            if (Element == null) return new Size(0, 0);
            return new Size(Element.Width, Element.Height);
        }
    }
}
