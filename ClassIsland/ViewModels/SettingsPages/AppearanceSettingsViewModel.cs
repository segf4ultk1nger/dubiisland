using System.Collections.Generic;
using System.Windows.Media;
using ClassIsland.Core;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ClassIsland.ViewModels.SettingsPages;

public class AppearanceSettingsViewModel : ObservableRecipient
{
    public List<FontFamily> FontFamilies { get; } = [..Fonts.SystemFontFamilies];
}