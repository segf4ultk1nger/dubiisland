using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace ClassIsland.Core.Controls;

/// <summary>
/// Shows one child at a time. Replaces the Material transitioner used by wizards.
/// Wipe animations are not played.
/// </summary>
public class Transitioner : Selector
{
    public static readonly RoutedCommand MoveNextCommand = new(nameof(MoveNextCommand), typeof(Transitioner));

    public static readonly RoutedCommand MovePreviousCommand = new(nameof(MovePreviousCommand), typeof(Transitioner));

    static Transitioner()
    {
        CommandManager.RegisterClassCommandBinding(
            typeof(Transitioner),
            new CommandBinding(MoveNextCommand, OnMoveNext, OnCanMoveNext));
        CommandManager.RegisterClassCommandBinding(
            typeof(Transitioner),
            new CommandBinding(MovePreviousCommand, OnMovePrevious, OnCanMovePrevious));
    }

    public Transitioner()
    {
        SelectedIndex = 0;
    }

    private static void OnMoveNext(object sender, ExecutedRoutedEventArgs e)
    {
        var transitioner = (Transitioner)sender;
        if (transitioner.SelectedIndex < transitioner.Items.Count - 1)
        {
            transitioner.SelectedIndex++;
        }
    }

    private static void OnCanMoveNext(object sender, CanExecuteRoutedEventArgs e)
    {
        var transitioner = (Transitioner)sender;
        e.CanExecute = transitioner.SelectedIndex < transitioner.Items.Count - 1;
    }

    private static void OnMovePrevious(object sender, ExecutedRoutedEventArgs e)
    {
        var transitioner = (Transitioner)sender;
        if (transitioner.SelectedIndex > 0)
        {
            transitioner.SelectedIndex--;
        }
    }

    private static void OnCanMovePrevious(object sender, CanExecuteRoutedEventArgs e)
    {
        var transitioner = (Transitioner)sender;
        e.CanExecute = transitioner.SelectedIndex > 0;
    }
}
