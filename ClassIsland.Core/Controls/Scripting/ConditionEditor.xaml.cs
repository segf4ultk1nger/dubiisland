using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using ClassIsland.Core.Abstractions.Services;
using ClassIsland.Shared;

namespace ClassIsland.Core.Controls.Scripting;

/// <summary>
/// ConditionEditor.xaml 的交互逻辑。用于编辑脚本条件表达式字符串。
/// </summary>
public partial class ConditionEditor : UserControl
{
    private static readonly Brush TrueBrush = new SolidColorBrush(Color.FromRgb(0x2E, 0xA0, 0x43));
    private static readonly Brush FalseBrush = new SolidColorBrush(Color.FromRgb(0xC0, 0x39, 0x2B));

    private readonly DispatcherTimer _previewTimer;
    private IScriptConditionEvaluator? _evaluator;
    private bool _evaluatorResolved;

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

    private IScriptConditionEvaluator? Evaluator
    {
        get
        {
            if (!_evaluatorResolved)
            {
                _evaluatorResolved = true;
                try
                {
                    _evaluator = IAppHost.GetService<IScriptConditionEvaluator>();
                }
                catch
                {
                    _evaluator = null;
                }
            }

            return _evaluator;
        }
    }

    /// <inheritdoc />
    public ConditionEditor()
    {
        _previewTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        _previewTimer.Tick += (_, _) =>
        {
            _previewTimer.Stop();
            UpdatePreview();
        };

        InitializeComponent();
    }

    private void ConditionTextBox_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        _previewTimer.Stop();
        _previewTimer.Start();
    }

    private void UpdatePreview()
    {
        var evaluator = Evaluator;
        var text = Condition;
        if (evaluator == null || string.IsNullOrWhiteSpace(text))
        {
            PreviewTextBlock.Text = "--";
            PreviewTextBlock.ClearValue(TextBlock.ForegroundProperty);
            PreviewErrorTextBlock.Visibility = Visibility.Collapsed;
            return;
        }

        try
        {
            var result = evaluator.EvaluatePreview(text);
            PreviewTextBlock.Text = result ? "true" : "false";
            PreviewTextBlock.Foreground = result ? TrueBrush : FalseBrush;
            PreviewErrorTextBlock.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            PreviewTextBlock.Text = "--";
            PreviewTextBlock.ClearValue(TextBlock.ForegroundProperty);
            PreviewErrorTextBlock.Text = "预览错误：" + ex.Message;
            PreviewErrorTextBlock.Visibility = Visibility.Visible;
        }
    }
}
