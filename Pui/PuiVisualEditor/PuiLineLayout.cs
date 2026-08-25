using Polaris.UI.Wire;
using System;
using System.Collections.Generic;
using System.Windows;

namespace PolarisTools.Pui.PuiVisualEditor
{
    public class PuiLineInfo
    {
        public PuiLineAlign Align { get; set; }
        public List<PuiElement> Elements { get; } = new List<PuiElement>();
        public Dictionary<PuiElement, Point> Positions { get; } = new Dictionary<PuiElement, Point>();
        // 跟 Positions 一一对应的"这个元素在这次布局里实际占用的宽高"。分割线/复合网格控件/滑条的有效
        // 尺寸是跟着换行光标状态算出来的（不是 elem.Width/Height 本身），画布和命中测试要用同一个值，
        // 不能各自再调用一次 PuiLineLayout.GetEffectiveSize 重算——那样分割线会重算不出正确宽度。
        public Dictionary<PuiElement, Size> Sizes { get; } = new Dictionary<PuiElement, Size>();
        public Rect Bounds { get; set; }
        public double ContentWidth { get; set; }
        public int StartIndex { get; set; }
        public int EndIndex { get; set; }
        public PuiElement LeadingMarker { get; set; }
    }

    public static class PuiLineLayout
    {
        // 对应真实游戏 XX.Designer.item_margin_x_px_ / item_margin_y_px_ 的默认值（dnSpy 反编译
        // unsafeAssem.dll 取得），不是随便猜的 4px——同行控件横向间距、换行纵向间距在真实引擎里是两个
        // 不同的常量，构造时会覆盖 DesignerRow 自身的 2/2 默认值。
        // ItemSpacingX 是 internal：PuiPreviewRenderer.RenderSlider 画"主滑条 + 间距 + setter"两段式
        // 时必须用跟 GetSliderEffectiveSize 完全相同的间距，否则 setter 会跟布局算出的外框对不上。
        internal const double ItemSpacingX = 14;
        private const double ItemSpacingY = 18;

        // 分割线（Separator）的高度公式跟宽度公式无关，Compute() 的换行光标分支和下面这个无状态版本
        // 都要用同一个数：line_height + margin_t + margin_b 一起折算进喂给通用换行光标的高度
        // （对应反编译到的 DesignerHr.get_sheight_px()）。
        private static double GetSeparatorHeight(PuiElement elem)
            => Math.Max(0, elem.LineHeight + elem.MarginBefore + elem.MarginAfter);

        // 复选框组/单选组/多按钮的 Width/Height 在真机里是"每一项"的宽高，不是容器整体外框——容器整体
        // 外框由 XX.Designer.reboundCarrForBtnMulti（dnSpy 反编译 unsafeAssem.dll 取得）按列数/项数/间距
        // 重新算出来：
        //   clms = Columns<=0 ? 项数 : Min(Columns, 项数)；rows = Ceil(项数 / clms)
        //   containerW = (Width*clms + MarginW*(clms-1)) * Scale
        //   containerH = (Height*rows + MarginH*(rows-1)) * Scale
        // （ButtonMulti 真机没有整体 scale 这个概念，固定按 1 处理。）
        private static (double Width, double Height) GetGridContainerSize(PuiElement elem)
        {
            string list = elem.ElementType == PuiElementType.ButtonMulti ? elem.Titles : elem.Keys;
            int itemCount = CountItems(list);

            int columns = elem.Columns <= 0 ? itemCount : Math.Min(elem.Columns, itemCount);
            if (columns < 1) columns = 1;
            int rows = (int)Math.Ceiling(itemCount / (double)columns);

            double scale = elem.ElementType == PuiElementType.ButtonMulti ? 1 : elem.Scale;
            double w = (elem.Width * columns + elem.MarginW * (columns - 1)) * scale;
            double h = (elem.Height * rows + elem.MarginH * (rows - 1)) * scale;
            return (Math.Max(0, w), Math.Max(0, h));
        }

        private static int CountItems(string semicolonSeparated)
        {
            if (string.IsNullOrWhiteSpace(semicolonSeparated)) return 1;
            return Math.Max(1, semicolonSeparated.Split(';').Length);
        }

        // NumCounter 真机是"每一位数字一个独立方块横向平铺"的转轮，不是单一方框：反编译
        // XX.Designer.addNumCounterT -> BtnContainerNumCounter.initNumCounter（dnSpy 反编译
        // unsafeAssem.dll 取得）确认位数取 Max(Digit 属性, 按 MaxVal 算出的十进制位数)，算法是
        // num=10,d=1，只要 num<=maxval 就 num*=10,d++——跟 Digit 属性同名但含义不同：Digit 只是
        // "至少要留几位"的下限，真正显示的位数还要看 MaxVal 能不能撑满这个位数。方块之间没有
        // margin，整体宽度=每位宽度×位数，跟 ButtonMulti/Checks/Radio 那种带间距的网格不一样。
        // 公开成 public 是因为 PuiPreviewRenderer.RenderNumCounter 要拿同一个数字去把 rect
        // 切成一格一格——两处必须用同一份实现，不能各自算一遍，否则位数一多就会跟布局宽度对不上。
        public static int GetNumCounterDigitCount(PuiElement elem)
        {
            int d = 1;
            for (long num = 10; num <= elem.MaxVal; num *= 10) d++;
            return Math.Max(elem.Digit, d);
        }

        private static (double Width, double Height) GetNumCounterEffectiveSize(PuiElement elem)
        {
            double digitWidth = elem.Width > 0 ? elem.Width : 24;
            double digitHeight = elem.Height > 0 ? elem.Height : 48;
            return (digitWidth * GetNumCounterDigitCount(elem), digitHeight);
        }

        // Slider 在真机里不是单个方块：nel.UiBoxDesigner.addSliderCT（dnSpy 反编译 Assembly-CSharp.dll
        // 取得）会在主滑条后面用单独一次 addItem 把数值 setter（默认宽 114，PuiElement.SetterWidth）接到
        // 同一行——对换行光标来说等价于"主滑条 + 一个标准 item 间距 + setter"两个相邻元素。skin 为
        // "invisible" 时真机会把主滑条宽度强制改成 1（交互全部交给 setter）。
        private static (double Width, double Height) GetSliderEffectiveSize(PuiElement elem)
        {
            bool invisible = string.Equals(elem.Skin, "invisible", StringComparison.Ordinal);
            double meterWidth = invisible ? 1 : elem.Width;
            double w = meterWidth + ItemSpacingX + Math.Max(0, elem.SetterWidth);
            return (w, elem.Height);
        }

        // 除分割线以外的类型都不依赖换行光标状态，可以无状态算出"有效尺寸"；分割线的占位宽度必须知道
        // 当前行还剩多少可用宽度，只有 Compute() 的主循环里才有这个信息，所以这里的分割线分支只是一个
        // "没有光标上下文时"的退化近似（比如渲染器在拿不到 Sizes 缓存时的兜底），真正布局时 Compute()
        // 会绕开这里、直接用光标位置算。
        public static (double Width, double Height) GetEffectiveSize(PuiElement elem, double windowWidth)
        {
            switch (elem.ElementType)
            {
                case PuiElementType.Separator:
                {
                    double w = elem.Vertical ? elem.Height : windowWidth;
                    return (Math.Max(0, w), GetSeparatorHeight(elem));
                }
                case PuiElementType.ButtonMulti:
                case PuiElementType.Checks:
                case PuiElementType.Radio:
                    return GetGridContainerSize(elem);
                case PuiElementType.Slider:
                    return GetSliderEffectiveSize(elem);
                case PuiElementType.NumCounter:
                    return GetNumCounterEffectiveSize(elem);
                default:
                    return (elem.Width, elem.Height);
            }
        }

        public static List<PuiLineInfo> Compute(PuiElement parent, double startX, double startY, double maxWidth, double windowWidth)
        {
            // 属性面板的宽高文本框是逐字符更新的，用户删掉重打的过程中会短暂出现
            // Width 小于 8（甚至更极端的值）之类的中间状态，此时 maxWidth 可能是负数；
            // new Rect(...) 不接受负的 width/height，不夹到非负就会直接把整个预览崩掉。
            maxWidth = Math.Max(0, maxWidth);

            var lines = new List<PuiLineInfo>();
            var children = parent.Children;

            var currentAlign = PuiLineAlign.Left;
            PuiElement pendingMarker = null;

            PuiLineInfo current = null;
            double currentX = startX;
            double currentY = startY;
            double rowHeight = 0;

            void Flush()
            {
                if (current == null) return;
                double lineWidth = Math.Min(current.ContentWidth, maxWidth);
                double offsetX = current.Align switch
                {
                    PuiLineAlign.Center => Math.Max(0, (maxWidth - lineWidth) / 2),
                    PuiLineAlign.Right => Math.Max(0, maxWidth - lineWidth),
                    _ => 0
                };

                foreach (var el in current.Elements)
                {
                    var p = current.Positions[el];
                    current.Positions[el] = new Point(p.X + offsetX, p.Y);
                }
                current.Bounds = new Rect(startX, currentY, maxWidth, rowHeight);
                lines.Add(current);
                current = null;
            }

            void EnsureLine(int index)
            {
                if (current != null) return;
                current = new PuiLineInfo { Align = currentAlign, StartIndex = index, LeadingMarker = pendingMarker };
                pendingMarker = null;
            }

            void PlaceSized(PuiElement child, int index, double width, double height)
            {
                EnsureLine(index);
                current.Elements.Add(child);
                current.Positions[child] = new Point(currentX, currentY);
                current.Sizes[child] = new Size(Math.Max(0, width), Math.Max(0, height));
                current.ContentWidth = Math.Max(current.ContentWidth, (currentX - startX) + width);
                current.EndIndex = index;
            }

            for (int i = 0; i < children.Count; i++)
            {
                var child = children[i];

                if (PuiElement.IsMarker(child.ElementType))
                {
                    currentAlign = child.ElementType == PuiElementType.DefaultLineStyle
                        ? PuiLineAlign.Left
                        : child.LineAlign;
                    if (current == null) pendingMarker = child;
                    continue;
                }

                if (child.ElementType == PuiElementType.LineBreak)
                {
                    // 真实 Designer.Br() 是纯控制流标记，从不放置任何有宽高的方块，且当前行是空的时候
                    // （row_w<=0）在真实引擎里也是个 no-op（不换行、不占位）。这里只保留"结算当前行 +
                    // 换行推进"这一个效果；LineBreak 自己不计入行宽/行高，只留一个可点选的位置方便编辑。
                    if (current != null)
                    {
                        current.Elements.Add(child);
                        current.Positions[child] = new Point(currentX, currentY);
                        current.Sizes[child] = new Size(child.Width, child.Height);
                        current.EndIndex = i;
                        Flush();
                        currentY += rowHeight + ItemSpacingY;
                        currentX = startX;
                        rowHeight = 0;
                    }
                    continue;
                }

                if (child.ElementType == PuiElementType.Separator && !child.Vertical)
                {
                    // 反编译确认：真机 addHr 在 swidth<=0（非竖线）时会先自动 Br() 换到新的一行，再用
                    // Designer.use_w（这一整行的可用宽度）当占位宽度——分割线永远独占一行、永远撑满，
                    // 跟 Ratio 无关（Ratio 只影响绘制覆盖率，见 PuiTreeWalker.cs 的对应注释）。这里先把
                    // 当前行（如果有内容）结算掉，再把分割线放到一条全新的、宽度=maxWidth 的行上；后面
                    // 紧跟着的元素会因为 currentX 已经越过 maxWidth 而自然触发下面的换行判断，等价于
                    // 分割线"独占一行"，不需要额外的"强制换行"标记。
                    if (current != null)
                    {
                        Flush();
                        currentY += rowHeight + ItemSpacingY;
                        currentX = startX;
                        rowHeight = 0;
                    }

                    double sepHeight = GetSeparatorHeight(child);
                    PlaceSized(child, i, maxWidth, sepHeight);
                    currentX += maxWidth + ItemSpacingX;
                    rowHeight = Math.Max(rowHeight, sepHeight);
                    continue;
                }

                var (effWidth, effHeight) = GetEffectiveSize(child, windowWidth);

                if (current != null && currentX > startX && currentX + effWidth > startX + maxWidth)
                {
                    Flush();
                    currentY += rowHeight + ItemSpacingY;
                    currentX = startX;
                    rowHeight = 0;
                }

                PlaceSized(child, i, effWidth, effHeight);
                currentX += effWidth + ItemSpacingX;
                rowHeight = Math.Max(rowHeight, effHeight);
            }

            Flush();
            return lines;
        }
    }
}
