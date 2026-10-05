using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media;
using ClassIsland.Core;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ClassIsland.ViewModels.SettingsPages;

public partial class AppearanceSettingsViewModel : ObservableRecipient
{
    public List<FontFamily> FontFamilies { get; } = [..Fonts.SystemFontFamilies];

    [ObservableProperty] private string _fontPreviewText = LoadFontPreview();

    private static string LoadFontPreview()
    {
        try
        {
            var stream = Application.GetResourceStream(new Uri("/Assets/FontPreview.txt", UriKind.Relative))?.Stream;
            if (stream == null)
            {
                return "";
            }

            using var reader = new StreamReader(stream, Encoding.UTF8);
            return reader.ReadToEnd().TrimEnd('\r', '\n');
        }
        catch
        {
            return "";
        }
    }
}
