using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.VisualStudio.Shell;
using Polaris.Magic.Authoring;

namespace PolarisTools.Magic.DefinitionEditor;

/// <summary>左栏一个基本属性。数值类型只有整数和浮点两种，因此用一个字段模型覆盖全部九项。</summary>
public partial class MagicBaseFieldViewModel : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NeedsValue))]
    private string text = "0";

    public MagicBaseFieldViewModel(string name, string label, bool required, bool integer)
    {
        Name = name;
        Label = label;
        Required = required;
        Integer = integer;
    }

    /// <summary><c>.pmagic</c> 里的属性名，也是回写文档时的分派键。</summary>
    public string Name { get; }

    public string Label { get; }

    /// <summary>必填项在标题后显示星号。</summary>
    public bool Required { get; }

    public bool Integer { get; }

    public string Header => Required ? Label + " *" : Label;

    /// <summary>
    /// 这个框里填的不是一个数。
    ///
    /// 不区分必填与选填：存盘时读不出数值的框一律按 0 写出，也就是说任何一个填坏了的框都会丢内容。
    /// 星号只表示"这一项没有可依赖的默认值"，提示的是"这里得填个数"。
    /// </summary>
    public bool NeedsValue => Integer
        ? !MagicPropertyValue.TryParseInt(Text ?? string.Empty, out _)
        : !MagicPropertyValue.TryParseFloat(Text ?? string.Empty, out _);
}

/// <summary>右栏一行自定义静态属性。</summary>
public partial class MagicPropertyRowViewModel : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NeedsName))]
    private string name = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NeedsValue))]
    private string value = "0";

    private MagicPropertyType type = MagicPropertyType.Int;

    /// <summary>改型时按新类型重置 Value：留着旧类型的文本只会变成一个填坏了的格子。</summary>
    public MagicPropertyType Type
    {
        get => type;
        set
        {
            if (SetProperty(ref type, value))
            {
                Value = MagicPropertyValue.DefaultText(value);
                OnPropertyChanged(nameof(NeedsValue));
            }
        }
    }

    /// <summary>名字直接变成生成代码里的属性名，所以必须是个合法 C# 标识符。</summary>
    public bool NeedsName => !MagicIdentifier.IsValidName(Name);

    public bool NeedsValue => !MagicPropertyValue.IsValid(Type, Value ?? string.Empty);

    public IReadOnlyList<MagicPropertyType> TypeChoices { get; } = new[]
    {
        MagicPropertyType.Int,
        MagicPropertyType.Float,
        MagicPropertyType.Bool,
        MagicPropertyType.String,
    };
}

/// <summary>
/// <c>.pmagic</c> 定义编辑器的数据模型。
///
/// 只有两栏：左边是原版需要的九个基本数值，右边是作者自己的静态参数表。没有资源页、本地化页或
/// 可插拔属性页——那些东西在这一版里不由 <c>.pmagic</c> 承载。
///
/// 也没有诊断列表：这是一张十来个字段的属性表，不是编程语言。填坏的格子当场高亮，底下一行说清
/// 该填什么，就够了。文件本身读不下去时（XML 坏了、有认不出的元素、版本过新）只读打开，
/// 把那句话显示在顶部。
/// </summary>
public partial class PmagicEditorViewModel : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IdNeedsValue))]
    private string magicId = MagicDefinitionDocument.TemplateId;

    [ObservableProperty] private bool isDirty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditable))]
    private bool isReadOnly;

    /// <summary>顶部横幅：只在文件读不下去时有内容。</summary>
    [ObservableProperty] private string statusMessage = string.Empty;

    /// <summary>底部一行：当前有哪些格子需要填。清干净时为空，那一行整体隐藏。</summary>
    [ObservableProperty] private string hint = string.Empty;

    private bool suspendTracking;

    public PmagicEditorViewModel()
    {
        BaseFields = new ObservableCollection<MagicBaseFieldViewModel>
        {
            new MagicBaseFieldViewModel("MpCost", "MP Cost", true, true),
            new MagicBaseFieldViewModel("CastTime", "Cast Time (frames)", true, false),
            new MagicBaseFieldViewModel("MpCrystalizeRatio", "MP Crystalize Ratio", true, false),
            new MagicBaseFieldViewModel("NeutralCrystalizeRatio", "Neutral Crystalize Ratio", true, false),
            new MagicBaseFieldViewModel("PrepareTime", "Prepare Time (frames)", false, false),
            new MagicBaseFieldViewModel("ManaDrainLock", "Mana Drain Lock", false, false),
            new MagicBaseFieldViewModel("ProjectilePower", "Projectile Power", false, true),
            new MagicBaseFieldViewModel("ShotgunRatio", "Shotgun Ratio", false, false),
            new MagicBaseFieldViewModel("SuperArmorTiredTime", "Super Armor Tired Time", false, false),
        };

        foreach (MagicBaseFieldViewModel field in BaseFields)
        {
            field.PropertyChanged += OnChildChanged;
        }

        Properties.CollectionChanged += OnPropertiesChanged;
    }

    /// <summary>只读时整个编辑区域禁用。取反属性放在这里，省得为一个布尔值写一个转换器。</summary>
    public bool IsEditable => !IsReadOnly;

    /// <summary>Id 必须是至少两段的点分名——它要进注册表，还决定分配给玩家存档的数字 Id。</summary>
    public bool IdNeedsValue => !MagicIdentifier.IsValidMagicId(MagicId ?? string.Empty);

    public ObservableCollection<MagicBaseFieldViewModel> BaseFields { get; }

    public ObservableCollection<MagicPropertyRowViewModel> Properties { get; } = new();

    // ==================== 读 ====================

    public void LoadFromFile(string path)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        suspendTracking = true;
        try
        {
            string xml = File.Exists(path)
                ? File.ReadAllText(path)
                : MagicDefinitionDocument.CreateTemplate().ToXml();

            MagicDefinitionDocument document;
            try
            {
                document = MagicDefinitionDocument.Parse(xml);
            }
            catch (MagicFormatException ex)
            {
                // 读不下去：只读打开并把原因显示在顶部，不用一份空模板把作者的文件顶掉。
                IsReadOnly = true;
                StatusMessage = ex.Message;
                Hint = string.Empty;
                return;
            }

            Apply(document);
            IsReadOnly = false;
            StatusMessage = string.Empty;
            RefreshHint();
        }
        finally
        {
            suspendTracking = false;
            IsDirty = false;
        }
    }

    private void Apply(MagicDefinitionDocument document)
    {
        MagicId = document.Id;

        SetField("MpCost", MagicPropertyValue.FormatInt(document.MpCost));
        SetField("CastTime", MagicPropertyValue.FormatFloat(document.CastTime));
        SetField("MpCrystalizeRatio", MagicPropertyValue.FormatFloat(document.MpCrystalizeRatio));
        SetField("NeutralCrystalizeRatio", MagicPropertyValue.FormatFloat(document.NeutralCrystalizeRatio));
        SetField("PrepareTime", MagicPropertyValue.FormatFloat(document.PrepareTime));
        SetField("ManaDrainLock", MagicPropertyValue.FormatFloat(document.ManaDrainLock));
        SetField("ProjectilePower", MagicPropertyValue.FormatInt(document.ProjectilePower));
        SetField("ShotgunRatio", MagicPropertyValue.FormatFloat(document.ShotgunRatio));
        SetField("SuperArmorTiredTime", MagicPropertyValue.FormatFloat(document.SuperArmorTiredTime));

        Properties.Clear();
        foreach (MagicCustomProperty property in document.Properties)
        {
            var row = new MagicPropertyRowViewModel { Name = property.Name };

            // 先设类型会重置 Value，所以顺序固定为"类型 → 值"。
            row.Type = property.Type;
            row.Value = property.Value;
            row.PropertyChanged += OnChildChanged;
            Properties.Add(row);
        }
    }

    // ==================== 写 ====================

    /// <summary>把当前编辑值写成规范 XML。填坏的格子按 0 写出——底下那行提示已经指出它们了。</summary>
    public void SaveToFile(string path)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        if (IsReadOnly)
        {
            return;
        }

        File.WriteAllText(path, ToDocument().ToXml(), new UTF8Encoding(false));
        IsDirty = false;
        RefreshHint();
    }

    public MagicDefinitionDocument ToDocument()
    {
        var document = new MagicDefinitionDocument
        {
            Version = MagicFormatVersion.Current,
            Id = MagicId ?? string.Empty,
            MpCost = Int("MpCost"),
            CastTime = Float("CastTime"),
            MpCrystalizeRatio = Float("MpCrystalizeRatio"),
            NeutralCrystalizeRatio = Float("NeutralCrystalizeRatio"),
            PrepareTime = Float("PrepareTime"),
            ManaDrainLock = Float("ManaDrainLock"),
            ProjectilePower = Int("ProjectilePower"),
            ShotgunRatio = Float("ShotgunRatio"),
            SuperArmorTiredTime = Float("SuperArmorTiredTime"),
        };

        foreach (MagicPropertyRowViewModel row in Properties)
        {
            document.Properties.Add(new MagicCustomProperty
            {
                Name = row.Name ?? string.Empty,
                Type = row.Type,
                Value = row.Value ?? string.Empty,
            });
        }

        return document;
    }

    // ==================== 编辑 ====================

    public void AddProperty()
    {
        var row = new MagicPropertyRowViewModel { Name = NextPropertyName() };
        row.PropertyChanged += OnChildChanged;
        Properties.Add(row);
    }

    public void RemoveProperty(MagicPropertyRowViewModel? row)
    {
        if (row == null)
        {
            return;
        }

        row.PropertyChanged -= OnChildChanged;
        Properties.Remove(row);
    }

    /// <summary>行顺序就是生成的属性顺序，所以上移/下移是有意义的编辑操作，不只是排版。</summary>
    public void MoveProperty(MagicPropertyRowViewModel row, int delta)
    {
        int index = Properties.IndexOf(row);
        int target = index + delta;
        if (index < 0 || target < 0 || target >= Properties.Count)
        {
            return;
        }

        Properties.Move(index, target);
    }

    // ==================== 提示 ====================

    /// <summary>重算底部那一行提示。只说"哪里要填什么"，不分级也不写错误码。</summary>
    public void RefreshHint()
    {
        var parts = new List<string>();

        if (IdNeedsValue)
        {
            parts.Add("Id needs at least two dot-separated segments, for example mymod.fireball.");
        }

        var numbers = new List<string>();
        foreach (MagicBaseFieldViewModel field in BaseFields)
        {
            if (field.NeedsValue)
            {
                numbers.Add(field.Label);
            }
        }

        if (numbers.Count > 0)
        {
            parts.Add("Needs a number: " + string.Join(", ", numbers) + ".");
        }

        var names = new List<string>();
        var values = new List<string>();
        foreach (MagicPropertyRowViewModel row in Properties)
        {
            if (row.NeedsName)
            {
                names.Add(string.IsNullOrEmpty(row.Name) ? "(unnamed)" : row.Name);
            }
            else if (row.NeedsValue)
            {
                values.Add(row.Name);
            }
        }

        if (names.Count > 0)
        {
            parts.Add("Custom property names must be C# identifiers: " + string.Join(", ", names) + ".");
        }

        if (values.Count > 0)
        {
            parts.Add("These custom property values do not match their type: " + string.Join(", ", values) + ".");
        }

        Hint = string.Join("  ", parts);
    }

    // ==================== 辅助 ====================

    private void OnChildChanged(object? sender, PropertyChangedEventArgs e)
    {
        MarkDirty();
        RefreshHint();
    }

    private void OnPropertiesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        MarkDirty();
        RefreshHint();
    }

    private void MarkDirty()
    {
        if (!suspendTracking && !IsReadOnly)
        {
            IsDirty = true;
        }
    }

    private MagicBaseFieldViewModel? Field(string name)
    {
        foreach (MagicBaseFieldViewModel field in BaseFields)
        {
            if (string.Equals(field.Name, name, StringComparison.Ordinal))
            {
                return field;
            }
        }

        return null;
    }

    private void SetField(string name, string text)
    {
        MagicBaseFieldViewModel? field = Field(name);
        if (field != null)
        {
            field.Text = text;
        }
    }

    /// <summary>取不出合法数值时回落到 0：底下那行提示已经指出这个框了，不必再让存盘失败。</summary>
    private int Int(string name) =>
        MagicPropertyValue.TryParseInt(Field(name)?.Text ?? string.Empty, out int value) ? value : 0;

    private float Float(string name) =>
        MagicPropertyValue.TryParseFloat(Field(name)?.Text ?? string.Empty, out float value) ? value : 0f;

    private string NextPropertyName()
    {
        for (int index = 1; ; index++)
        {
            string candidate = "Property" + index;
            bool taken = false;
            foreach (MagicPropertyRowViewModel row in Properties)
            {
                if (string.Equals(row.Name, candidate, StringComparison.Ordinal))
                {
                    taken = true;
                    break;
                }
            }

            if (!taken)
            {
                return candidate;
            }
        }
    }
}
