using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using Polaris.Addons.Authoring;

namespace PolarisTools.Addons.DefinitionEditor;

public partial class AddonDefinitionEditorViewModel : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsItem))]
    [NotifyPropertyChangedFor(nameof(IsPlugin))]
    [NotifyPropertyChangedFor(nameof(IsSkill))]
    [NotifyPropertyChangedFor(nameof(TypeLabel))]
    private AddonDocumentKind kind;

    [ObservableProperty] private string id = string.Empty;
    [ObservableProperty] private string itemId = string.Empty;
    [ObservableProperty] private string nameKey = string.Empty;
    [ObservableProperty] private string titleKey = string.Empty;
    [ObservableProperty] private string descriptionKey = string.Empty;
    [ObservableProperty] private string icon = string.Empty;
    [ObservableProperty] private string behaviorType = string.Empty;
    [ObservableProperty] private string price = "0";
    [ObservableProperty] private string stackLimit = "1";
    [ObservableProperty] private string category = "Other";
    [ObservableProperty] private string cost = "1";
    [ObservableProperty] private AddonSkillMode skillMode = AddonSkillMode.Passive;
    [ObservableProperty] private AddonSkillUnlockPolicy unlockPolicy = AddonSkillUnlockPolicy.ConsumeOwnerItem;
    [ObservableProperty] private string cooldownSeconds = "0";
    [ObservableProperty] private string concurrencyGroup = string.Empty;
    [ObservableProperty] private bool isDirty;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditable))]
    private bool isReadOnly;
    [ObservableProperty] private string statusMessage = string.Empty;
    [ObservableProperty] private string hint = string.Empty;

    private bool suspendTracking;

    public AddonDefinitionEditorViewModel()
    {
        PropertyChanged += OnAnyPropertyChanged;
    }

    public bool IsItem => Kind == AddonDocumentKind.Item;

    public bool IsPlugin => Kind == AddonDocumentKind.Plugin;

    public bool IsSkill => Kind == AddonDocumentKind.Skill;

    public bool IsEditable => !IsReadOnly;

    public string TypeLabel => Kind switch
    {
        AddonDocumentKind.Item => ".pitem · item root",
        AddonDocumentKind.Plugin => ".pplugin · enhancer facet",
        _ => ".pskill · skill facet",
    };

    public IReadOnlyList<AddonSkillMode> SkillModes { get; } = new[]
    {
        AddonSkillMode.Passive,
        AddonSkillMode.Active,
        AddonSkillMode.Toggle,
    };

    public IReadOnlyList<AddonSkillUnlockPolicy> UnlockPolicies { get; } = new[]
    {
        AddonSkillUnlockPolicy.OwnItem,
        AddonSkillUnlockPolicy.ConsumeOwnerItem,
        AddonSkillUnlockPolicy.External,
    };

    public void LoadFromFile(string path)
    {
        suspendTracking = true;
        try
        {
            Kind = KindOf(path);
            string xml = File.Exists(path) ? File.ReadAllText(path) : Template(Kind).ToXmlText();
            try
            {
                Apply(Parse(Kind, xml));
                IsReadOnly = false;
                StatusMessage = string.Empty;
                RefreshHint();
            }
            catch (AddonFormatException ex)
            {
                IsReadOnly = true;
                StatusMessage = ex.Message;
                Hint = string.Empty;
            }
        }
        finally
        {
            suspendTracking = false;
            IsDirty = false;
        }
    }

    public void SaveToFile(string path)
    {
        if (IsReadOnly)
        {
            return;
        }

        File.WriteAllText(path, BuildDocument().ToXmlText(), new UTF8Encoding(false));
        IsDirty = false;
        RefreshHint();
    }

    public void RefreshHint()
    {
        var messages = new List<string>();
        if (!AddonIdentifier.IsValidId(Id))
        {
            messages.Add("Id needs a lowercase namespace, for example mymod.item.");
        }

        if (!IsItem && !AddonIdentifier.IsValidId(ItemId))
        {
            messages.Add("Item Id must point to a .pitem definition.");
        }

        if (!AddonIdentifier.IsValidOptionalTypeName(BehaviorType))
        {
            messages.Add("Behavior type must be a fully qualified C# type name.");
        }

        if (IsItem && (!TryInt(Price, out int itemPrice) || itemPrice < 0
            || !TryInt(StackLimit, out int limit) || limit < 1))
        {
            messages.Add("Price must be non-negative and stack limit must be at least 1.");
        }

        if (IsPlugin && (!TryInt(Cost, out int pluginCost) || pluginCost < 0))
        {
            messages.Add("Plugin slot cost must be a non-negative integer.");
        }

        if (IsSkill && (!TryDouble(CooldownSeconds, out double cooldown) || cooldown < 0))
        {
            messages.Add("Skill cooldown must be a non-negative number.");
        }
        if (IsSkill && !string.IsNullOrEmpty(ConcurrencyGroup) && !AddonIdentifier.IsValidId(ConcurrencyGroup))
        {
            messages.Add("Concurrency group must be empty or a valid Addons id.");
        }

        Hint = string.Join("  ", messages);
    }

    private AddonDefinitionDocument BuildDocument()
    {
        switch (Kind)
        {
            case AddonDocumentKind.Item:
                return new ItemDefinitionDocument
                {
                    Version = AddonFormatVersion.Current,
                    Id = Id ?? string.Empty,
                    NameKey = NameKey ?? string.Empty,
                    DescriptionKey = DescriptionKey ?? string.Empty,
                    Icon = Icon ?? string.Empty,
                    Price = TryInt(Price, out int priceValue) ? priceValue : 0,
                    StackLimit = TryInt(StackLimit, out int stackValue) ? stackValue : 1,
                    Category = Category ?? string.Empty,
                    BehaviorType = BehaviorType ?? string.Empty,
                };
            case AddonDocumentKind.Plugin:
                return new PluginDefinitionDocument
                {
                    Version = AddonFormatVersion.Current,
                    Id = Id ?? string.Empty,
                    ItemId = ItemId ?? string.Empty,
                    TitleKey = TitleKey ?? string.Empty,
                    DescriptionKey = DescriptionKey ?? string.Empty,
                    Icon = Icon ?? string.Empty,
                    Cost = TryInt(Cost, out int costValue) ? costValue : 0,
                    BehaviorType = BehaviorType ?? string.Empty,
                };
            default:
                return new SkillDefinitionDocument
                {
                    Version = AddonFormatVersion.Current,
                    Id = Id ?? string.Empty,
                    ItemId = ItemId ?? string.Empty,
                    TitleKey = TitleKey ?? string.Empty,
                    DescriptionKey = DescriptionKey ?? string.Empty,
                    Icon = Icon ?? string.Empty,
                    Mode = SkillMode,
                    Unlock = UnlockPolicy,
                    CooldownSeconds = TryDouble(CooldownSeconds, out double cooldown) ? cooldown : 0,
                    ConcurrencyGroup = ConcurrencyGroup ?? string.Empty,
                    BehaviorType = BehaviorType ?? string.Empty,
                };
        }
    }

    private void Apply(AddonDefinitionDocument document)
    {
        Id = document.Id;
        BehaviorType = document.BehaviorType;

        switch (document)
        {
            case ItemDefinitionDocument item:
                NameKey = item.NameKey;
                DescriptionKey = item.DescriptionKey;
                Icon = item.Icon;
                Price = item.Price.ToString(CultureInfo.InvariantCulture);
                StackLimit = item.StackLimit.ToString(CultureInfo.InvariantCulture);
                Category = item.Category;
                break;
            case PluginDefinitionDocument plugin:
                ItemId = plugin.ItemId;
                TitleKey = plugin.TitleKey;
                DescriptionKey = plugin.DescriptionKey;
                Icon = plugin.Icon;
                Cost = plugin.Cost.ToString(CultureInfo.InvariantCulture);
                break;
            case SkillDefinitionDocument skill:
                ItemId = skill.ItemId;
                TitleKey = skill.TitleKey;
                DescriptionKey = skill.DescriptionKey;
                Icon = skill.Icon;
                SkillMode = skill.Mode;
                UnlockPolicy = skill.Unlock;
                CooldownSeconds = skill.CooldownSeconds.ToString("R", CultureInfo.InvariantCulture);
                ConcurrencyGroup = skill.ConcurrencyGroup;
                break;
        }
    }

    private void OnAnyPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (suspendTracking || args.PropertyName == nameof(IsDirty)
            || args.PropertyName == nameof(Hint) || args.PropertyName == nameof(StatusMessage))
        {
            return;
        }

        if (!IsReadOnly)
        {
            IsDirty = true;
            RefreshHint();
        }
    }

    private static bool TryInt(string value, out int result) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out result);

    private static bool TryDouble(string value, out double result) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result);

    private static AddonDocumentKind KindOf(string path)
    {
        string extension = Path.GetExtension(path);
        if (string.Equals(extension, ".pitem", StringComparison.OrdinalIgnoreCase)) return AddonDocumentKind.Item;
        if (string.Equals(extension, ".pplugin", StringComparison.OrdinalIgnoreCase)) return AddonDocumentKind.Plugin;
        if (string.Equals(extension, ".pskill", StringComparison.OrdinalIgnoreCase)) return AddonDocumentKind.Skill;
        throw new AddonFormatException("Unsupported Polaris Addons file extension '" + extension + "'.");
    }

    private static AddonDefinitionDocument Parse(AddonDocumentKind kind, string xml) => kind switch
    {
        AddonDocumentKind.Item => ItemDefinitionDocument.Parse(xml),
        AddonDocumentKind.Plugin => PluginDefinitionDocument.Parse(xml),
        _ => SkillDefinitionDocument.Parse(xml),
    };

    private static AddonDefinitionDocument Template(AddonDocumentKind kind) => kind switch
    {
        AddonDocumentKind.Item => ItemDefinitionDocument.CreateTemplate(),
        AddonDocumentKind.Plugin => PluginDefinitionDocument.CreateTemplate(),
        _ => SkillDefinitionDocument.CreateTemplate(),
    };
}

internal static class AddonDocumentXmlExtensions
{
    internal static string ToXmlText(this AddonDefinitionDocument document) => document switch
    {
        ItemDefinitionDocument item => item.ToXml(),
        PluginDefinitionDocument plugin => plugin.ToXml(),
        SkillDefinitionDocument skill => skill.ToXml(),
        _ => throw new AddonFormatException("Unsupported Addons document type."),
    };
}
