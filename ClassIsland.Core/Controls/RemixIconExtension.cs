using System.Windows.Markup;
using MahApps.Metro.IconPacks;

namespace ClassIsland.Core.Controls;

/// <summary>
/// Creates a <see cref="RemixIconText"/> for a RemixIcon. Use with <c>{x:Static remix:PackIconRemixIconKind.Name}</c>.
/// </summary>
[MarkupExtensionReturnType(typeof(RemixIconText))]
public class RemixIconExtension : MarkupExtension
{
    public RemixIconExtension()
    {
    }

    public RemixIconExtension(PackIconRemixIconKind kind)
    {
        Kind = kind;
    }

    public PackIconRemixIconKind Kind { get; set; }

    public double Size { get; set; } = 16;

    public override object ProvideValue(IServiceProvider serviceProvider) =>
        new RemixIconText
        {
            Kind = Kind,
            IconSize = Size
        };
}
