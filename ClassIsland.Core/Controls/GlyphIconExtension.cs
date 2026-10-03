using System.Windows.Markup;

namespace ClassIsland.Core.Controls;

/// <summary>
/// Creates an <see cref="IconText"/> for a Segoe MDL2 glyph.
/// Use with <c>{x:Static controls:IconGlyphs.Name}</c>.
/// </summary>
[MarkupExtensionReturnType(typeof(IconText))]
public class GlyphIconExtension : MarkupExtension
{
    public GlyphIconExtension()
    {
    }

    public GlyphIconExtension(string glyph)
    {
        Glyph = glyph;
    }

    public string Glyph { get; set; } = "";

    public double Size { get; set; } = 16;

    public override object ProvideValue(IServiceProvider serviceProvider) =>
        new IconText
        {
            Kind = Glyph,
            IconSize = Size
        };
}
