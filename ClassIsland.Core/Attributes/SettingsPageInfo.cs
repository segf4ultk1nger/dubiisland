using ClassIsland.Core.Enums.SettingsWindow;

using ClassIsland.Core.Controls;
namespace ClassIsland.Core.Attributes;

[AttributeUsage(AttributeTargets.Class)]
public class SettingsPageInfo : Attribute
{
    public string Name { get; } = "";
    public string Id { get; } = "";
    public string UnSelectedPackIcon { get; private set; } = IconGlyphs.CogOutline;
    public string SelectedPackIcon { get; private set; } = IconGlyphs.Cog;
    public string UnSelectedBitmapUri { get; private set; } = "";
    public string SelectedBitmapUri { get; private set; } = "";
    public bool UseBitmapIcon { get; private set; } = false;

    public bool HideDefault { get; } = false;

    public SettingsPageCategory Category { get; } = SettingsPageCategory.External;
    
    public SettingsPageInfo(string id, string name, SettingsPageCategory category=SettingsPageCategory.External)
    {
        Id = id;
        Name = name;
        Category = category;
    }

    public SettingsPageInfo(string id, string name, bool hideDefault, SettingsPageCategory category = SettingsPageCategory.External) : this(id, name, category)
    {
        HideDefault = hideDefault;
    }

    public SettingsPageInfo(string id, string name, string unSelected, string selected, SettingsPageCategory category = SettingsPageCategory.External) : this(id, name, category)
    {
        ApplyIcons(unSelected, selected);
    }

    public SettingsPageInfo(string id, string name, string unSelected, string selected, bool hideDefault, SettingsPageCategory category = SettingsPageCategory.External) : this(id, name, unSelected, selected, category)
    {
        HideDefault = hideDefault;
    }

    void ApplyIcons(string unSelected, string selected)
    {
        if (IsGlyph(unSelected) && IsGlyph(selected))
        {
            UnSelectedPackIcon = unSelected;
            SelectedPackIcon = selected;
            return;
        }

        UnSelectedBitmapUri = unSelected;
        SelectedBitmapUri = selected;
        UseBitmapIcon = true;
    }

    static bool IsGlyph(string value) =>
        value.Length is > 0 and <= 2 && value.IndexOfAny(['/', '\\', '.']) < 0;
}