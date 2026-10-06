using System.Windows;
using System.Windows.Controls;

namespace ClassIsland.Core.Controls.Scripting;

/// <summary>
/// ConditionEditor.xaml 的交互逻辑。用于编辑脚本条件表达式字符串。
/// </summary>
public partial class ConditionEditor : UserControl
{
    public static readonly DependencyProperty ConditionProperty = DependencyProperty.Register(
        nameof(Condition), typeof(string), typeof(ConditionEditor), new FrameworkPropertyMetadata("",
            FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    /// <summary>
    /// 条件表达式。
    /// </summary>
    public string? Condition
    {
        get => (string?)GetValue(ConditionProperty);
        set => SetValue(ConditionProperty, value);
    }

    /// <inheritdoc />
    public ConditionEditor()
    {
        InitializeComponent();
    }
}
