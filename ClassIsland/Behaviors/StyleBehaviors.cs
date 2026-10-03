using System.Windows;
using Microsoft.Xaml.Behaviors;

namespace ClassIsland.Behaviors;

/// <summary>
/// Freezable behavior list that can be assigned from a style setter.
/// </summary>
public class StyleBehaviorCollection : FreezableCollection<Behavior>
{
    protected override Freezable CreateInstanceCore() => new StyleBehaviorCollection();
}

/// <summary>
/// Attaches a <see cref="StyleBehaviorCollection"/> without Material behaviors assist.
/// Each target receives cloned behavior instances.
/// </summary>
public static class StyleBehaviors
{
    public static readonly DependencyProperty BehaviorsProperty = DependencyProperty.RegisterAttached(
        "Behaviors",
        typeof(StyleBehaviorCollection),
        typeof(StyleBehaviors),
        new PropertyMetadata(null, OnBehaviorsChanged));

    public static void SetBehaviors(DependencyObject element, StyleBehaviorCollection? value) =>
        element.SetValue(BehaviorsProperty, value);

    public static StyleBehaviorCollection? GetBehaviors(DependencyObject element) =>
        (StyleBehaviorCollection?)element.GetValue(BehaviorsProperty);

    private static void OnBehaviorsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is not StyleBehaviorCollection collection)
        {
            return;
        }

        var behaviors = Interaction.GetBehaviors(d);
        foreach (var behavior in collection)
        {
            if (behavior.Clone() is Behavior copy)
            {
                behaviors.Add(copy);
            }
        }
    }
}
