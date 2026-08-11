using Polaris.PUI.Wire;
using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Xml.Linq;

namespace PolarisTools.Pui.PuiVisualEditor
{
    public enum PuiElementType
    {
        Window,
        Button,
        Text,
        LineBreak,
        Separator,
        LineStyle,
        DefaultLineStyle,
        ButtonMulti,
        Checks,
        Radio,
        Slider,
        Input,
        NumCounter,
        ColorCell,
        Image
    }





    /// <summary>
    /// 单个"可注册为回调"的属性，包一层视图模型：把某个 PuiElement 字段（OnClick/OnChanged/...）
    /// 统一暴露成 MethodName/IsBound，供右侧"回调"Tab 用一份通用 ItemsControl 渲染，不用再按
    /// ElementType 各写一份 XAML 块。转发 owner 的 PropertyChanged，让绑定的文本框/按钮跟着字段变化刷新。
    /// </summary>
    public sealed class PuiCallbackHook : ObservableObject
    {
        private readonly PuiElement _owner;
        private readonly string _backingPropertyName;
        private readonly Func<PuiElement, string> _getter;
        private readonly Action<PuiElement, string> _setter;

        public string HookKind { get; }
        public string DisplayLabel { get; }

        internal PuiCallbackHook(PuiElement owner, string hookKind, string displayLabel, string backingPropertyName,
            Func<PuiElement, string> getter, Action<PuiElement, string> setter)
        {
            _owner = owner;
            HookKind = hookKind;
            DisplayLabel = displayLabel;
            _backingPropertyName = backingPropertyName;
            _getter = getter;
            _setter = setter;
            owner.PropertyChanged += OnOwnerPropertyChanged;
        }

        private void OnOwnerPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != _backingPropertyName) return;
            OnPropertyChanged(nameof(MethodName));
            OnPropertyChanged(nameof(IsBound));
        }

        public string MethodName
        {
            get => _getter(_owner) ?? "";
            set => _setter(_owner, value ?? "");
        }

        public bool IsBound => !string.IsNullOrEmpty(MethodName);
    }

    public partial class PuiElement : ObservableObject
    {
        [ObservableProperty]
        private string _name = "";

        [ObservableProperty]
        private double _width = 100;

        [ObservableProperty]
        private double _height = 30;

        [ObservableProperty]
        private string _text = "";

        [ObservableProperty]
        private bool _isSelected;

        [ObservableProperty]
        private bool _isLineSelected;

        [ObservableProperty]
        private PuiLineAlign _lineAlign = PuiLineAlign.Left;

        [ObservableProperty]
        private PuiElement _parent;

        // Window：对应 UiBoxDesignerFamily.Create(...) + UiBoxDesigner 的面板级配置。
        [ObservableProperty]
        private double _pixelX;

        [ObservableProperty]
        private double _pixelY;

        [ObservableProperty]
        private int _appearDir = -1;

        [ObservableProperty]
        private double _appearLen = 30;

        [ObservableProperty]
        private PuiMaskType _mask = PuiMaskType.Box;

        [ObservableProperty]
        private PuiFrameType _frameType = PuiFrameType.Main;

        [ObservableProperty]
        private bool _focusable;

        // Text：对应 DsnDataP（P/addP）。
        [ObservableProperty]
        private PuiTextAlign _align = PuiTextAlign.Left;

        [ObservableProperty]
        private bool _html;

        [ObservableProperty]
        private string _color = ""; // TxCol（文字颜色）/ Col（分割线颜色），十六进制 RRGGBBAA，按 ElementType 给不同默认值

        [ObservableProperty]
        private string _backgroundColor = ""; // DsnDataP.Col（Text 背景色）

        [ObservableProperty]
        private string _borderColor = ""; // DsnDataP.TxBorderCol（Text 描边色）

        [ObservableProperty]
        private double _size; // DsnDataP.size（字号，0=自动）

        [ObservableProperty]
        private double _lineSpacing = -1; // DsnDataP.lineSpacing（-1=不覆盖）

        [ObservableProperty]
        private double _letterSpacing = -1; // DsnDataP.letterSpacing（-1=不覆盖）

        // Button / ButtonMulti / Checks / Radio / Slider / Input / NumCounter / ColorCell 共用：皮肤名。
        [ObservableProperty]
        private string _skin = "";

        [ObservableProperty]
        private string _skinTitle = ""; // Slider.skin_title / ColorCell.skin_title

        // Separator：对应 DsnDataHr（addHr）。
        [ObservableProperty]
        private bool _vertical;

        [ObservableProperty]
        private double _ratio = 0.6; // 面板宽度的比例；生成时换算成像素填入 swidth

        [ObservableProperty]
        private double _marginBefore = 18; // margin_t / margin_l

        [ObservableProperty]
        private double _marginAfter = 26; // margin_b / margin_r

        [ObservableProperty]
        private double _lineHeight = 1;

        [ObservableProperty]
        private double _dashedLength; // DsnDataHr.dashed_oneline_lgt

        [ObservableProperty]
        private double _drawWidthRate = 0.75; // DsnDataHr.draw_width_rate

        // 回调 hook：方法名留空表示不生成对应字段赋值；缺失的方法桩由 package 侧自动追加。
        [ObservableProperty]
        private string _onClick = ""; // FnBtnBindings：bool (aBtn)

        [ObservableProperty]
        private string _onChanged = ""; // Radio.fnChanged / Slider.fnChanged / Input.fnChanged，签名按类型不同

        [ObservableProperty]
        private string _onChangedDelay = ""; // Input.fnChangedDelay

        // 注意：不能叫 OnColorChanged —— CommunityToolkit.Mvvm 会给 Color 属性自动生成一个同名的
        // OnColorChanged(oldValue,newValue) 变更回调方法，两者会撞名（CS0102）。
        [ObservableProperty]
        private string _onColorPromptDone = ""; // ColorCell.fnPromptDone

        [ObservableProperty]
        private string _onBuildCompleted = ""; // Window 专属：BuildUI 执行完所有子元素语句后触发，签名 void Method(UiBoxDesigner designer)

        // ButtonMulti：对应 DsnDataButtonMulti（addButtonMultiT）。
        [ObservableProperty]
        private string _titles = ""; // ';' 分隔

        [ObservableProperty]
        private int _defMask; // ButtonMulti.def / Checks.def 位掩码

        [ObservableProperty]
        private int _lockedMask; // ButtonMulti.locked 位掩码

        // Checks / Radio：对应 DsnDataChecks / DsnDataRadio（addChecksT / addRadioT）。
        [ObservableProperty]
        private string _keys = ""; // ';' 分隔

        [ObservableProperty]
        private string _descs = ""; // ';' 分隔，可选

        [ObservableProperty]
        private double _scale = 1; // Checks.scale / Radio.scale / Image.scale

        [ObservableProperty]
        private int _columns; // ButtonMulti.clms / Checks.clms / Radio.clms

        [ObservableProperty]
        private double _marginW = 30; // ButtonMulti/Checks/Radio 的 margin_w

        [ObservableProperty]
        private double _marginH = 18; // ButtonMulti/Checks/Radio 的 margin_h

        [ObservableProperty]
        private int _naviLoop; // ButtonMulti/Checks/Radio/NumCounter 的 navi_loop

        // Radio 专属。
        [ObservableProperty]
        private double _def; // Radio.def（索引）/ Slider.def（数值）/ NumCounter.def（整数），按类型转换

        [ObservableProperty]
        private bool _valueReturnName;

        [ObservableProperty]
        private bool _allFunctionSame;

        [ObservableProperty]
        private bool _rowMode; // 为真时链式调用 .RowMode(skin)

        // Slider：对应 DsnDataSlider（UiBoxDesigner.addSliderCT）。
        [ObservableProperty]
        private double _min; // Slider.mn / Input.min，按类型转换

        [ObservableProperty]
        private double _max = 1; // Slider.mx / Input.max，按类型转换（Input 默认另设，见构造函数）

        [ObservableProperty]
        private double _step = 1; // Slider.valintv

        [ObservableProperty]
        private bool _submitHolding;

        [ObservableProperty]
        private int _checkboxMode; // 0/1/2

        [ObservableProperty]
        private string _descKeys = ""; // Slider.Adesc_keys，';' 分隔

        [ObservableProperty]
        private double _setterWidth = 114; // 仅传给 addSliderCT

        // Input：对应 DsnDataInput（addInput）。
        [ObservableProperty]
        private string _label = "";

        [ObservableProperty]
        private double _boundsWidth;

        [ObservableProperty]
        private int _fontSize; // Input.size（0=用 Designer.default_input_size）

        [ObservableProperty]
        private int _maxLen = -1;

        [ObservableProperty]
        private bool _integer;

        [ObservableProperty]
        private bool _hexInteger;

        [ObservableProperty]
        private bool _number;

        [ObservableProperty]
        private int _multiLine = 1;

        [ObservableProperty]
        private bool _labelTop;

        [ObservableProperty]
        private bool _returnBlur = true;

        [ObservableProperty]
        private bool _editable = true;

        [ObservableProperty]
        private bool _allocEmpty = true;

        [ObservableProperty]
        private int _changedDelayMaxT = 60;

        // NumCounter：对应 DsnDataNumCounter（addNumCounterT）。
        [ObservableProperty]
        private bool _locked;

        [ObservableProperty]
        private int _minVal;

        [ObservableProperty]
        private int _maxVal = 999;

        [ObservableProperty]
        private int _digit;

        [ObservableProperty]
        private bool _slideCurDigitOnly;

        // ColorCell：对应 DsnDataColorCell（addColorCell）。
        [ObservableProperty]
        private string _defColor = "FFFFFFFF";

        [ObservableProperty]
        private bool _openPrompt = true;

        [ObservableProperty]
        private bool _useText = true;

        [ObservableProperty]
        private bool _useAlpha = true;

        // Image：对应 DsnDataImg（addImg）。只支持 PolarisRes 原始图片（MI），不支持 PXLS
        // 角色帧（PF）。图片来源有两种写法，ImageResource 优先：
        //
        //   ImageResource —— 属性面板里选出来的那个字段引用（形如 MyMod.Res.testImage），指向
        //     模组自己那个打了 [PolarisResource] 的 static MImage 字段。PolarisRes 的
        //     AutoBindScanner 启动时已经按 [PolarisResourceFolder] 挂载目录、把 MImage 填进
        //     那个字段了，PUI 只是把它读出来，不需要再挂一次目录、也不会重复解码。编辑器能
        //     顺着这个引用找到磁盘上的图片文件，所以画布上画的是真实图片（见 PolarisResourceCatalog）。
        //
        //   ImageSource —— 早于资源字段选择器存在的写法：PolarisRes 挂载相对路径，运行时经
        //     PolarisResAPI.For(<当前程序集名>).Own.Image(...) 解析。它要求模组自己手动
        //     MountDefault/Mount 过那个目录（自动绑定用的是按类分开的挂载表，不是 modId 那张
        //     共享表），属性面板里也从来没露出过这个字段，只可能是手写 XML 填的——因此保留读写
        //     与生成能力，不再作为推荐路径。
        [ObservableProperty]
        private string _imageResource = "";

        [ObservableProperty]
        private string _imageSource = "";

        [ObservableProperty]
        private bool _stencilLessEqual = true;

        [ObservableProperty]
        private double _uvX;

        [ObservableProperty]
        private double _uvY;

        [ObservableProperty]
        private double _uvW = 1;

        [ObservableProperty]
        private double _uvH = 1;

        public static bool IsMarker(PuiElementType type) =>
            type == PuiElementType.LineStyle || type == PuiElementType.DefaultLineStyle;

        public PuiElementType ElementType { get; }
        public ObservableCollection<PuiElement> Children { get; } = new ObservableCollection<PuiElement>();

        // Window 专属："状态连接点"列表，见 PuiStateTransition。跟 Children 一样是集合类型，
        // 不用 [ObservableProperty]（那是给单值字段用的）。
        public ObservableCollection<PuiStateTransition> StateTransitions { get; } = new ObservableCollection<PuiStateTransition>();

        // 当前元素类型能注册哪些回调；与 PolarisPuiGenerator.GetHandlerSignature /
        // CollectRequiredHandlers 用的是同一套 hook 种类字符串，三处改动时要对齐。
        public IReadOnlyList<PuiCallbackHook> CallbackHooks { get; }

        public PuiElement(PuiElementType type)
        {
            ElementType = type;
            switch (type)
            {
                case PuiElementType.Window:
                    Width = 400;
                    Height = 300;
                    Name = "Window1";
                    break;
                case PuiElementType.Button:
                    Width = 100;
                    Height = 30;
                    Text = "Button";
                    Name = "Button1";
                    break;
                case PuiElementType.Text:
                    Width = 200;
                    Height = 24;
                    Text = "Text";
                    Name = "Text1";
                    Color = "FFFFFFFF";
                    BackgroundColor = "00000000";
                    BorderColor = "00000000";
                    break;
                case PuiElementType.LineBreak:
                    Width = 24;
                    Height = 24;
                    Name = "";
                    break;
                case PuiElementType.Separator:
                    Width = 0;
                    Height = 16;
                    Name = "";
                    Color = "000000BE"; // (0,0,0,190)
                    break;
                // 行样式标记本身不占空间、也不需要名字，只是遍历时的一个控制流标记。
                case PuiElementType.LineStyle:
                case PuiElementType.DefaultLineStyle:
                    Width = 0;
                    Height = 0;
                    Name = "";
                    break;
                case PuiElementType.ButtonMulti:
                    Width = 200;
                    Height = 32;
                    Name = "ButtonMulti1";
                    Skin = "normal";
                    Titles = "Option1;Option2";
                    break;
                case PuiElementType.Checks:
                    Width = 140;
                    Height = 24;
                    Name = "Checks1";
                    Skin = "checkbox_string";
                    Keys = "a;b";
                    Columns = 1;
                    break;
                case PuiElementType.Radio:
                    Width = 140;
                    Height = 24;
                    Name = "Radio1";
                    Skin = "radio_string";
                    Keys = "a;b";
                    break;
                case PuiElementType.Slider:
                    Width = 160;
                    Height = 28;
                    Name = "Slider1";
                    Skin = "normal";
                    Text = "Slider";
                    break;
                case PuiElementType.Input:
                    Width = 160;
                    Height = 28;
                    Name = "Input1";
                    Min = -2147483648;
                    Max = 2147483647;
                    break;
                case PuiElementType.NumCounter:
                    Width = 120;
                    Height = 32;
                    Name = "NumCounter1";
                    Skin = "normal";
                    break;
                case PuiElementType.ColorCell:
                    Width = 70;
                    Height = 20;
                    Name = "ColorCell1";
                    Skin = "colorcell";
                    break;
                case PuiElementType.Image:
                    Width = 64;
                    Height = 64;
                    Name = "Image1";
                    break;
            }

            CallbackHooks = BuildCallbackHooks();
        }

        private List<PuiCallbackHook> BuildCallbackHooks()
        {
            var hooks = new List<PuiCallbackHook>();
            void Add(string kind, string label, string backingProperty, Func<PuiElement, string> get, Action<PuiElement, string> set)
                => hooks.Add(new PuiCallbackHook(this, kind, label, backingProperty, get, set));

            switch (ElementType)
            {
                case PuiElementType.Window:
                    Add("OnBuildCompleted", "OnBuildCompleted (build finished)", nameof(OnBuildCompleted),
                        e => e.OnBuildCompleted, (e, v) => e.OnBuildCompleted = v);
                    break;

                case PuiElementType.Button:
                case PuiElementType.ButtonMulti:
                case PuiElementType.Checks:
                case PuiElementType.NumCounter:
                    Add("OnClick", "OnClick (click)", nameof(OnClick), e => e.OnClick, (e, v) => e.OnClick = v);
                    break;

                case PuiElementType.Radio:
                case PuiElementType.Slider:
                    Add("OnClick", "OnClick (click)", nameof(OnClick), e => e.OnClick, (e, v) => e.OnClick = v);
                    Add("OnChanged", "OnChanged (changed)", nameof(OnChanged), e => e.OnChanged, (e, v) => e.OnChanged = v);
                    break;

                case PuiElementType.Input:
                    Add("OnChanged", "OnChanged (content changed)", nameof(OnChanged), e => e.OnChanged, (e, v) => e.OnChanged = v);
                    Add("OnChangedDelay", "OnChangedDelay (delayed change)", nameof(OnChangedDelay), e => e.OnChangedDelay, (e, v) => e.OnChangedDelay = v);
                    break;

                case PuiElementType.ColorCell:
                    Add("OnColorChanged", "OnColorChanged (color changed)", nameof(OnColorPromptDone), e => e.OnColorPromptDone, (e, v) => e.OnColorPromptDone = v);
                    break;
            }

            return hooks;
        }

        public XElement ToXml()
        {
            var elem = new XElement(ElementType.ToString());
            if (ElementType == PuiElementType.LineStyle)
            {
                elem.SetAttributeValue("Align", LineAlign.ToString());
                return elem;
            }
            if (ElementType == PuiElementType.DefaultLineStyle)
                return elem;

            // Window 的 Name 不再落盘：生成/热重载时统一用 .pui 文件名本身（见 PolarisPuiGenerator.cs
            // 的 GenerateCSharp、PuiVisualEditorControl.xaml.cs 的 HotReload_Click），不需要用户
            // 单独维护一个跟文件名重复、还可能对不上的字段。
            if (!string.IsNullOrEmpty(Name) && ElementType != PuiElementType.Window) elem.SetAttributeValue("Name", Name);
            if (ElementType != PuiElementType.LineBreak && ElementType != PuiElementType.Separator)
            {
                elem.SetAttributeValue("Width", Width);
                elem.SetAttributeValue("Height", Height);
            }
            if (!string.IsNullOrEmpty(Text) && ElementType != PuiElementType.Window)
                elem.SetAttributeValue("Text", Text);

            switch (ElementType)
            {
                case PuiElementType.Window:
                    if (PixelX != 0) elem.SetAttributeValue("PixelX", PixelX);
                    if (PixelY != 0) elem.SetAttributeValue("PixelY", PixelY);
                    if (AppearDir != -1) elem.SetAttributeValue("AppearDir", AppearDir);
                    if (AppearLen != 30) elem.SetAttributeValue("AppearLen", AppearLen);
                    if (Mask != PuiMaskType.Box) elem.SetAttributeValue("Mask", Mask.ToString());
                    if (FrameType != PuiFrameType.Main) elem.SetAttributeValue("FrameType", FrameType.ToString());
                    if (Focusable) elem.SetAttributeValue("Focusable", Focusable);
                    if (!string.IsNullOrEmpty(OnBuildCompleted)) elem.SetAttributeValue("OnBuildCompleted", OnBuildCompleted);
                    if (StateTransitions.Count > 0)
                    {
                        var transitionsElem = new XElement("StateTransitions");
                        foreach (var t in StateTransitions)
                        {
                            var te = new XElement("Transition");
                            te.SetAttributeValue("Id", t.Id);
                            te.SetAttributeValue("Trigger", t.TriggerType.ToString());
                            if (t.TriggerType == PuiStateTriggerType.ButtonClick) te.SetAttributeValue("Button", t.ButtonName);
                            if (t.TriggerType == PuiStateTriggerType.CustomEvent) te.SetAttributeValue("EventKey", t.EventKey);
                            te.SetAttributeValue("Blocking", t.Blocking);
                            transitionsElem.Add(te);
                        }
                        elem.Add(transitionsElem);
                    }
                    break;
                case PuiElementType.Text:
                    if (Align != PuiTextAlign.Left) elem.SetAttributeValue("Align", Align.ToString());
                    if (Html) elem.SetAttributeValue("Html", Html);
                    if (!string.IsNullOrEmpty(Color) && Color != "FFFFFFFF") elem.SetAttributeValue("Color", Color);
                    if (!string.IsNullOrEmpty(BackgroundColor) && BackgroundColor != "00000000") elem.SetAttributeValue("BackgroundColor", BackgroundColor);
                    if (!string.IsNullOrEmpty(BorderColor) && BorderColor != "00000000") elem.SetAttributeValue("BorderColor", BorderColor);
                    if (Size != 0) elem.SetAttributeValue("Size", Size);
                    if (LineSpacing != -1) elem.SetAttributeValue("LineSpacing", LineSpacing);
                    if (LetterSpacing != -1) elem.SetAttributeValue("LetterSpacing", LetterSpacing);
                    break;
                case PuiElementType.Button:
                    if (!string.IsNullOrEmpty(Skin)) elem.SetAttributeValue("Skin", Skin);
                    if (!string.IsNullOrEmpty(OnClick)) elem.SetAttributeValue("OnClick", OnClick);
                    break;
                case PuiElementType.Separator:
                    if (Vertical) elem.SetAttributeValue("Vertical", Vertical);
                    if (Ratio != 0.6) elem.SetAttributeValue("Ratio", Ratio);
                    if (MarginBefore != 18) elem.SetAttributeValue("MarginBefore", MarginBefore);
                    if (MarginAfter != 26) elem.SetAttributeValue("MarginAfter", MarginAfter);
                    if (LineHeight != 1) elem.SetAttributeValue("LineHeight", LineHeight);
                    if (!string.IsNullOrEmpty(Color) && Color != "000000BE") elem.SetAttributeValue("Color", Color);
                    if (DashedLength != 0) elem.SetAttributeValue("DashedLength", DashedLength);
                    if (DrawWidthRate != 0.75) elem.SetAttributeValue("DrawWidthRate", DrawWidthRate);
                    break;
                case PuiElementType.ButtonMulti:
                    if (!string.IsNullOrEmpty(Titles)) elem.SetAttributeValue("Titles", Titles);
                    if (!string.IsNullOrEmpty(Skin)) elem.SetAttributeValue("Skin", Skin);
                    if (Columns != 0) elem.SetAttributeValue("Columns", Columns);
                    if (MarginW != 30) elem.SetAttributeValue("MarginW", MarginW);
                    if (MarginH != 18) elem.SetAttributeValue("MarginH", MarginH);
                    if (NaviLoop != 0) elem.SetAttributeValue("NaviLoop", NaviLoop);
                    if (DefMask != 0) elem.SetAttributeValue("DefMask", DefMask);
                    if (LockedMask != 0) elem.SetAttributeValue("LockedMask", LockedMask);
                    if (!string.IsNullOrEmpty(OnClick)) elem.SetAttributeValue("OnClick", OnClick);
                    break;
                case PuiElementType.Checks:
                    if (!string.IsNullOrEmpty(Keys)) elem.SetAttributeValue("Keys", Keys);
                    if (!string.IsNullOrEmpty(Descs)) elem.SetAttributeValue("Descs", Descs);
                    if (!string.IsNullOrEmpty(Skin)) elem.SetAttributeValue("Skin", Skin);
                    if (Scale != 1) elem.SetAttributeValue("Scale", Scale);
                    if (Columns != 1) elem.SetAttributeValue("Columns", Columns);
                    if (MarginW != 30) elem.SetAttributeValue("MarginW", MarginW);
                    if (MarginH != 18) elem.SetAttributeValue("MarginH", MarginH);
                    if (NaviLoop != 0) elem.SetAttributeValue("NaviLoop", NaviLoop);
                    if (DefMask != 0) elem.SetAttributeValue("DefMask", DefMask);
                    if (!string.IsNullOrEmpty(OnClick)) elem.SetAttributeValue("OnClick", OnClick);
                    break;
                case PuiElementType.Radio:
                    if (!string.IsNullOrEmpty(Keys)) elem.SetAttributeValue("Keys", Keys);
                    if (!string.IsNullOrEmpty(Descs)) elem.SetAttributeValue("Descs", Descs);
                    if (!string.IsNullOrEmpty(Skin)) elem.SetAttributeValue("Skin", Skin);
                    if (Columns != 0) elem.SetAttributeValue("Columns", Columns);
                    if (Scale != 1) elem.SetAttributeValue("Scale", Scale);
                    if (MarginW != 30) elem.SetAttributeValue("MarginW", MarginW);
                    if (MarginH != 18) elem.SetAttributeValue("MarginH", MarginH);
                    if (Def != 0) elem.SetAttributeValue("Def", Def);
                    if (ValueReturnName) elem.SetAttributeValue("ValueReturnName", ValueReturnName);
                    if (AllFunctionSame) elem.SetAttributeValue("AllFunctionSame", AllFunctionSame);
                    if (NaviLoop != 0) elem.SetAttributeValue("NaviLoop", NaviLoop);
                    if (RowMode) elem.SetAttributeValue("RowMode", RowMode);
                    if (!string.IsNullOrEmpty(OnClick)) elem.SetAttributeValue("OnClick", OnClick);
                    if (!string.IsNullOrEmpty(OnChanged)) elem.SetAttributeValue("OnChanged", OnChanged);
                    break;
                case PuiElementType.Slider:
                    if (!string.IsNullOrEmpty(Skin)) elem.SetAttributeValue("Skin", Skin);
                    if (!string.IsNullOrEmpty(SkinTitle)) elem.SetAttributeValue("SkinTitle", SkinTitle);
                    if (Min != 0) elem.SetAttributeValue("Min", Min);
                    if (Max != 1) elem.SetAttributeValue("Max", Max);
                    if (Step != 1) elem.SetAttributeValue("Step", Step);
                    if (Def != 0) elem.SetAttributeValue("Def", Def);
                    if (SubmitHolding) elem.SetAttributeValue("SubmitHolding", SubmitHolding);
                    if (CheckboxMode != 0) elem.SetAttributeValue("CheckboxMode", CheckboxMode);
                    if (!string.IsNullOrEmpty(DescKeys)) elem.SetAttributeValue("DescKeys", DescKeys);
                    if (SetterWidth != 114) elem.SetAttributeValue("SetterWidth", SetterWidth);
                    if (!string.IsNullOrEmpty(OnClick)) elem.SetAttributeValue("OnClick", OnClick);
                    if (!string.IsNullOrEmpty(OnChanged)) elem.SetAttributeValue("OnChanged", OnChanged);
                    break;
                case PuiElementType.Input:
                    if (!string.IsNullOrEmpty(Label)) elem.SetAttributeValue("Label", Label);
                    if (!string.IsNullOrEmpty(Skin)) elem.SetAttributeValue("Skin", Skin);
                    if (BoundsWidth != 0) elem.SetAttributeValue("BoundsWidth", BoundsWidth);
                    if (FontSize != 0) elem.SetAttributeValue("FontSize", FontSize);
                    if (MaxLen != -1) elem.SetAttributeValue("MaxLen", MaxLen);
                    if (Min != -2147483648) elem.SetAttributeValue("Min", Min);
                    if (Max != 2147483647) elem.SetAttributeValue("Max", Max);
                    if (Integer) elem.SetAttributeValue("Integer", Integer);
                    if (HexInteger) elem.SetAttributeValue("HexInteger", HexInteger);
                    if (Number) elem.SetAttributeValue("Number", Number);
                    if (MultiLine != 1) elem.SetAttributeValue("MultiLine", MultiLine);
                    if (LabelTop) elem.SetAttributeValue("LabelTop", LabelTop);
                    if (!ReturnBlur) elem.SetAttributeValue("ReturnBlur", ReturnBlur);
                    if (!Editable) elem.SetAttributeValue("Editable", Editable);
                    if (!AllocEmpty) elem.SetAttributeValue("AllocEmpty", AllocEmpty);
                    if (ChangedDelayMaxT != 60) elem.SetAttributeValue("ChangedDelayMaxT", ChangedDelayMaxT);
                    if (!string.IsNullOrEmpty(OnChanged)) elem.SetAttributeValue("OnChanged", OnChanged);
                    if (!string.IsNullOrEmpty(OnChangedDelay)) elem.SetAttributeValue("OnChangedDelay", OnChangedDelay);
                    break;
                case PuiElementType.NumCounter:
                    if (!string.IsNullOrEmpty(Skin)) elem.SetAttributeValue("Skin", Skin);
                    if (Def != 0) elem.SetAttributeValue("Def", Def);
                    if (Locked) elem.SetAttributeValue("Locked", Locked);
                    if (MinVal != 0) elem.SetAttributeValue("MinVal", MinVal);
                    if (MaxVal != 999) elem.SetAttributeValue("MaxVal", MaxVal);
                    if (Digit != 0) elem.SetAttributeValue("Digit", Digit);
                    if (SlideCurDigitOnly) elem.SetAttributeValue("SlideCurDigitOnly", SlideCurDigitOnly);
                    if (NaviLoop != 0) elem.SetAttributeValue("NaviLoop", NaviLoop);
                    if (!string.IsNullOrEmpty(OnClick)) elem.SetAttributeValue("OnClick", OnClick);
                    break;
                case PuiElementType.ColorCell:
                    if (!string.IsNullOrEmpty(DefColor) && DefColor != "FFFFFFFF") elem.SetAttributeValue("DefColor", DefColor);
                    if (!OpenPrompt) elem.SetAttributeValue("OpenPrompt", OpenPrompt);
                    if (!UseText) elem.SetAttributeValue("UseText", UseText);
                    if (!UseAlpha) elem.SetAttributeValue("UseAlpha", UseAlpha);
                    if (!string.IsNullOrEmpty(Skin)) elem.SetAttributeValue("Skin", Skin);
                    if (!string.IsNullOrEmpty(SkinTitle)) elem.SetAttributeValue("SkinTitle", SkinTitle);
                    if (!string.IsNullOrEmpty(OnColorPromptDone)) elem.SetAttributeValue("OnColorChanged", OnColorPromptDone);
                    break;
                case PuiElementType.Image:
                    if (!string.IsNullOrEmpty(ImageResource)) elem.SetAttributeValue("ImageResource", ImageResource);
                    if (!string.IsNullOrEmpty(ImageSource)) elem.SetAttributeValue("ImageSource", ImageSource);
                    if (Scale != 1) elem.SetAttributeValue("Scale", Scale);
                    if (!StencilLessEqual) elem.SetAttributeValue("StencilLessEqual", StencilLessEqual);
                    if (UvX != 0) elem.SetAttributeValue("UvX", UvX);
                    if (UvY != 0) elem.SetAttributeValue("UvY", UvY);
                    if (UvW != 1) elem.SetAttributeValue("UvW", UvW);
                    if (UvH != 1) elem.SetAttributeValue("UvH", UvH);
                    break;
            }

            foreach (var child in Children)
                elem.Add(child.ToXml());
            return elem;
        }

        public static PuiElement FromXml(XElement elem, PuiElement parent = null)
        {
            if (!Enum.TryParse<PuiElementType>(elem.Name.LocalName, out var type))
                return null;
            var e = new PuiElement(type) { Parent = parent };
            if (type == PuiElementType.LineStyle)
            {
                e.LineAlign = Enum.TryParse<PuiLineAlign>((string)elem.Attribute("Align"), out var align)
                    ? align
                    : PuiLineAlign.Left;
                return e;
            }
            if (type == PuiElementType.DefaultLineStyle)
                return e;

            e.Name = (string)elem.Attribute("Name") ?? "";
            e.Width = (double?)elem.Attribute("Width") ?? e.Width;
            e.Height = (double?)elem.Attribute("Height") ?? e.Height;
            e.Text = (string)elem.Attribute("Text") ?? "";

            switch (type)
            {
                case PuiElementType.Window:
                    e.PixelX = (double?)elem.Attribute("PixelX") ?? e.PixelX;
                    e.PixelY = (double?)elem.Attribute("PixelY") ?? e.PixelY;
                    e.AppearDir = (int?)elem.Attribute("AppearDir") ?? e.AppearDir;
                    e.AppearLen = (double?)elem.Attribute("AppearLen") ?? e.AppearLen;
                    e.Mask = Enum.TryParse<PuiMaskType>((string)elem.Attribute("Mask"), out var mask) ? mask : e.Mask;
                    e.FrameType = Enum.TryParse<PuiFrameType>((string)elem.Attribute("FrameType"), out var frameType) ? frameType : e.FrameType;
                    e.Focusable = (bool?)elem.Attribute("Focusable") ?? e.Focusable;
                    e.OnBuildCompleted = (string)elem.Attribute("OnBuildCompleted") ?? "";
                    var transitionsElem = elem.Element("StateTransitions");
                    if (transitionsElem != null)
                    {
                        foreach (var te in transitionsElem.Elements("Transition"))
                        {
                            var trigger = Enum.TryParse<PuiStateTriggerType>((string)te.Attribute("Trigger"), out var tt)
                                ? tt : PuiStateTriggerType.ButtonClick;
                            e.StateTransitions.Add(new PuiStateTransition
                            {
                                Id = (string)te.Attribute("Id") ?? Guid.NewGuid().ToString("N"),
                                TriggerType = trigger,
                                ButtonName = (string)te.Attribute("Button") ?? "",
                                EventKey = (string)te.Attribute("EventKey") ?? "",
                                Blocking = (bool?)te.Attribute("Blocking") ?? true,
                            });
                        }
                    }
                    break;
                case PuiElementType.Text:
                    e.Align = Enum.TryParse<PuiTextAlign>((string)elem.Attribute("Align"), out var align2) ? align2 : e.Align;
                    e.Html = (bool?)elem.Attribute("Html") ?? e.Html;
                    e.Color = (string)elem.Attribute("Color") ?? e.Color;
                    e.BackgroundColor = (string)elem.Attribute("BackgroundColor") ?? e.BackgroundColor;
                    e.BorderColor = (string)elem.Attribute("BorderColor") ?? e.BorderColor;
                    e.Size = (double?)elem.Attribute("Size") ?? e.Size;
                    e.LineSpacing = (double?)elem.Attribute("LineSpacing") ?? e.LineSpacing;
                    e.LetterSpacing = (double?)elem.Attribute("LetterSpacing") ?? e.LetterSpacing;
                    break;
                case PuiElementType.Button:
                    e.Skin = (string)elem.Attribute("Skin") ?? "";
                    e.OnClick = (string)elem.Attribute("OnClick") ?? "";
                    break;
                case PuiElementType.Separator:
                    e.Vertical = (bool?)elem.Attribute("Vertical") ?? e.Vertical;
                    e.Ratio = (double?)elem.Attribute("Ratio") ?? e.Ratio;
                    e.MarginBefore = (double?)elem.Attribute("MarginBefore") ?? e.MarginBefore;
                    e.MarginAfter = (double?)elem.Attribute("MarginAfter") ?? e.MarginAfter;
                    e.LineHeight = (double?)elem.Attribute("LineHeight") ?? e.LineHeight;
                    e.Color = (string)elem.Attribute("Color") ?? e.Color;
                    e.DashedLength = (double?)elem.Attribute("DashedLength") ?? e.DashedLength;
                    e.DrawWidthRate = (double?)elem.Attribute("DrawWidthRate") ?? e.DrawWidthRate;
                    break;
                case PuiElementType.ButtonMulti:
                    e.Titles = (string)elem.Attribute("Titles") ?? "";
                    e.Skin = (string)elem.Attribute("Skin") ?? e.Skin;
                    e.Columns = (int?)elem.Attribute("Columns") ?? e.Columns;
                    e.MarginW = (double?)elem.Attribute("MarginW") ?? e.MarginW;
                    e.MarginH = (double?)elem.Attribute("MarginH") ?? e.MarginH;
                    e.NaviLoop = (int?)elem.Attribute("NaviLoop") ?? e.NaviLoop;
                    e.DefMask = (int?)elem.Attribute("DefMask") ?? e.DefMask;
                    e.LockedMask = (int?)elem.Attribute("LockedMask") ?? e.LockedMask;
                    e.OnClick = (string)elem.Attribute("OnClick") ?? "";
                    break;
                case PuiElementType.Checks:
                    e.Keys = (string)elem.Attribute("Keys") ?? e.Keys;
                    e.Descs = (string)elem.Attribute("Descs") ?? "";
                    e.Skin = (string)elem.Attribute("Skin") ?? e.Skin;
                    e.Scale = (double?)elem.Attribute("Scale") ?? e.Scale;
                    e.Columns = (int?)elem.Attribute("Columns") ?? e.Columns;
                    e.MarginW = (double?)elem.Attribute("MarginW") ?? e.MarginW;
                    e.MarginH = (double?)elem.Attribute("MarginH") ?? e.MarginH;
                    e.NaviLoop = (int?)elem.Attribute("NaviLoop") ?? e.NaviLoop;
                    e.DefMask = (int?)elem.Attribute("DefMask") ?? e.DefMask;
                    e.OnClick = (string)elem.Attribute("OnClick") ?? "";
                    break;
                case PuiElementType.Radio:
                    e.Keys = (string)elem.Attribute("Keys") ?? e.Keys;
                    e.Descs = (string)elem.Attribute("Descs") ?? "";
                    e.Skin = (string)elem.Attribute("Skin") ?? e.Skin;
                    e.Columns = (int?)elem.Attribute("Columns") ?? e.Columns;
                    e.Scale = (double?)elem.Attribute("Scale") ?? e.Scale;
                    e.MarginW = (double?)elem.Attribute("MarginW") ?? e.MarginW;
                    e.MarginH = (double?)elem.Attribute("MarginH") ?? e.MarginH;
                    e.Def = (double?)elem.Attribute("Def") ?? e.Def;
                    e.ValueReturnName = (bool?)elem.Attribute("ValueReturnName") ?? e.ValueReturnName;
                    e.AllFunctionSame = (bool?)elem.Attribute("AllFunctionSame") ?? e.AllFunctionSame;
                    e.NaviLoop = (int?)elem.Attribute("NaviLoop") ?? e.NaviLoop;
                    e.RowMode = (bool?)elem.Attribute("RowMode") ?? e.RowMode;
                    e.OnClick = (string)elem.Attribute("OnClick") ?? "";
                    e.OnChanged = (string)elem.Attribute("OnChanged") ?? "";
                    break;
                case PuiElementType.Slider:
                    e.Skin = (string)elem.Attribute("Skin") ?? e.Skin;
                    e.SkinTitle = (string)elem.Attribute("SkinTitle") ?? "";
                    e.Min = (double?)elem.Attribute("Min") ?? e.Min;
                    e.Max = (double?)elem.Attribute("Max") ?? e.Max;
                    e.Step = (double?)elem.Attribute("Step") ?? e.Step;
                    e.Def = (double?)elem.Attribute("Def") ?? e.Def;
                    e.SubmitHolding = (bool?)elem.Attribute("SubmitHolding") ?? e.SubmitHolding;
                    e.CheckboxMode = (int?)elem.Attribute("CheckboxMode") ?? e.CheckboxMode;
                    e.DescKeys = (string)elem.Attribute("DescKeys") ?? "";
                    e.SetterWidth = (double?)elem.Attribute("SetterWidth") ?? e.SetterWidth;
                    e.OnClick = (string)elem.Attribute("OnClick") ?? "";
                    e.OnChanged = (string)elem.Attribute("OnChanged") ?? "";
                    break;
                case PuiElementType.Input:
                    e.Label = (string)elem.Attribute("Label") ?? "";
                    e.Skin = (string)elem.Attribute("Skin") ?? e.Skin;
                    e.BoundsWidth = (double?)elem.Attribute("BoundsWidth") ?? e.BoundsWidth;
                    e.FontSize = (int?)elem.Attribute("FontSize") ?? e.FontSize;
                    e.MaxLen = (int?)elem.Attribute("MaxLen") ?? e.MaxLen;
                    e.Min = (double?)elem.Attribute("Min") ?? e.Min;
                    e.Max = (double?)elem.Attribute("Max") ?? e.Max;
                    e.Integer = (bool?)elem.Attribute("Integer") ?? e.Integer;
                    e.HexInteger = (bool?)elem.Attribute("HexInteger") ?? e.HexInteger;
                    e.Number = (bool?)elem.Attribute("Number") ?? e.Number;
                    e.MultiLine = (int?)elem.Attribute("MultiLine") ?? e.MultiLine;
                    e.LabelTop = (bool?)elem.Attribute("LabelTop") ?? e.LabelTop;
                    e.ReturnBlur = (bool?)elem.Attribute("ReturnBlur") ?? e.ReturnBlur;
                    e.Editable = (bool?)elem.Attribute("Editable") ?? e.Editable;
                    e.AllocEmpty = (bool?)elem.Attribute("AllocEmpty") ?? e.AllocEmpty;
                    e.ChangedDelayMaxT = (int?)elem.Attribute("ChangedDelayMaxT") ?? e.ChangedDelayMaxT;
                    e.OnChanged = (string)elem.Attribute("OnChanged") ?? "";
                    e.OnChangedDelay = (string)elem.Attribute("OnChangedDelay") ?? "";
                    break;
                case PuiElementType.NumCounter:
                    e.Skin = (string)elem.Attribute("Skin") ?? e.Skin;
                    e.Def = (double?)elem.Attribute("Def") ?? e.Def;
                    e.Locked = (bool?)elem.Attribute("Locked") ?? e.Locked;
                    e.MinVal = (int?)elem.Attribute("MinVal") ?? e.MinVal;
                    e.MaxVal = (int?)elem.Attribute("MaxVal") ?? e.MaxVal;
                    e.Digit = (int?)elem.Attribute("Digit") ?? e.Digit;
                    e.SlideCurDigitOnly = (bool?)elem.Attribute("SlideCurDigitOnly") ?? e.SlideCurDigitOnly;
                    e.NaviLoop = (int?)elem.Attribute("NaviLoop") ?? e.NaviLoop;
                    e.OnClick = (string)elem.Attribute("OnClick") ?? "";
                    break;
                case PuiElementType.ColorCell:
                    e.DefColor = (string)elem.Attribute("DefColor") ?? e.DefColor;
                    e.OpenPrompt = (bool?)elem.Attribute("OpenPrompt") ?? e.OpenPrompt;
                    e.UseText = (bool?)elem.Attribute("UseText") ?? e.UseText;
                    e.UseAlpha = (bool?)elem.Attribute("UseAlpha") ?? e.UseAlpha;
                    e.Skin = (string)elem.Attribute("Skin") ?? e.Skin;
                    e.SkinTitle = (string)elem.Attribute("SkinTitle") ?? "";
                    e.OnColorPromptDone = (string)elem.Attribute("OnColorChanged") ?? "";
                    break;
                case PuiElementType.Image:
                    e.ImageResource = (string)elem.Attribute("ImageResource") ?? "";
                    e.ImageSource = (string)elem.Attribute("ImageSource") ?? "";
                    e.Scale = (double?)elem.Attribute("Scale") ?? e.Scale;
                    e.StencilLessEqual = (bool?)elem.Attribute("StencilLessEqual") ?? e.StencilLessEqual;
                    e.UvX = (double?)elem.Attribute("UvX") ?? e.UvX;
                    e.UvY = (double?)elem.Attribute("UvY") ?? e.UvY;
                    e.UvW = (double?)elem.Attribute("UvW") ?? e.UvW;
                    e.UvH = (double?)elem.Attribute("UvH") ?? e.UvH;
                    break;
            }

            foreach (var childElem in elem.Elements())
            {
                var child = FromXml(childElem, e);
                if (child != null) e.Children.Add(child);
            }
            return e;
        }
    }
}
