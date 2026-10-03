using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MahApps.Metro.Controls;
using MahApps.Metro.Controls.Dialogs;

namespace ClassIsland.Core.Services;

/// <summary>
/// 在已注册的 <see cref="MetroWindow"/> 上显示对话框，并在关闭时返回命令参数。
/// </summary>
public static class DialogService
{
    /// <summary>
    /// 关闭当前对话框。<see cref="ExecutedRoutedEventArgs.Parameter"/>（即按钮的 CommandParameter）成为 <see cref="ShowAsync"/> 的结果。
    /// </summary>
    public static readonly RoutedCommand CloseDialogCommand = new(nameof(CloseDialogCommand), typeof(DialogService));

    /// <summary>
    /// 打开对话框。命令参数是要显示的内容。
    /// </summary>
    public static readonly RoutedCommand OpenDialogCommand = new(nameof(OpenDialogCommand), typeof(DialogService));

    public static readonly DependencyProperty IdentifierProperty = DependencyProperty.RegisterAttached(
        "Identifier",
        typeof(object),
        typeof(DialogService),
        new PropertyMetadata(null, OnIdentifierChanged));

    private static readonly Dictionary<string, MetroWindow> Windows = new();
    private static readonly Dictionary<MetroWindow, string> WindowIdentifiers = new();
    private static readonly Dictionary<string, DialogSession> OpenSessions = new();

    static DialogService()
    {
        CommandManager.RegisterClassCommandBinding(typeof(UIElement),
            new CommandBinding(CloseDialogCommand, OnCloseExecuted, OnCloseCanExecute));
        CommandManager.RegisterClassCommandBinding(typeof(UIElement),
            new CommandBinding(OpenDialogCommand, OnOpenExecuted, OnOpenCanExecute));
    }

    public static void SetIdentifier(DependencyObject element, object? value) => element.SetValue(IdentifierProperty, value);

    public static object? GetIdentifier(DependencyObject element) => element.GetValue(IdentifierProperty);

    /// <summary>
    /// 显示 <paramref name="content"/>，直到 <see cref="CloseDialogCommand"/> 或 <see cref="Close"/> 关闭它。
    /// 同一标识已有对话框时不再打开第二个，并返回 null。
    /// </summary>
    public static Task<object?> ShowAsync(object content, string? identifier)
    {
        if (content is null)
            throw new ArgumentNullException(nameof(content));

        var key = Key(identifier);
        if (!Windows.TryGetValue(key, out var window))
            throw new InvalidOperationException($"没有找到标识为“{identifier}”的对话框窗口。");

        if (!window.Dispatcher.CheckAccess())
            return window.Dispatcher.InvokeAsync(() => ShowCoreAsync(window, content, key)).Task.Unwrap();

        return ShowCoreAsync(window, content, key);
    }

    public static bool IsOpen(string? identifier) => OpenSessions.ContainsKey(Key(identifier));

    /// <summary>
    /// 在已注册的 <see cref="MetroWindow"/> 上使用 MahApps 的标准消息对话框显示标题、消息和按钮组。
    /// </summary>
    public static async Task<MessageDialogResult> ShowMessageAsync(string? identifier, string title, string message,
        MessageDialogStyle style = MessageDialogStyle.Affirmative, MetroDialogSettings? settings = null)
    {
        if (!Windows.TryGetValue(Key(identifier), out var window))
            throw new InvalidOperationException($"没有找到标识为“{identifier}”的对话框窗口。");

        await EnsureLoadedAsync(window);

        if (!window.Dispatcher.CheckAccess())
            return await window.Dispatcher.InvokeAsync(() => window.ShowMessageAsync(title, message, style, settings)).Task.Unwrap();

        return await window.ShowMessageAsync(title, message, style, settings);
    }

    /// <summary>
    /// 关闭指定标识的对话框，并以 <paramref name="result"/> 完成 <see cref="ShowAsync"/>。
    /// </summary>
    public static void Close(string? identifier, object? result)
    {
        var key = Key(identifier);
        if (!OpenSessions.TryGetValue(key, out var session) || session.CloseStarted)
            return;

        session.CloseStarted = true;
        _ = HideAsync(session, result);
    }

    public static bool TryGetIdentifier(MetroWindow? window, out string? identifier)
    {
        identifier = null;
        if (window == null)
            return false;
        if (!WindowIdentifiers.TryGetValue(window, out var id))
            return false;
        identifier = id;
        return true;
    }

    private static async Task<object?> ShowCoreAsync(MetroWindow window, object content, string key)
    {
        await EnsureLoadedAsync(window);

        if (OpenSessions.ContainsKey(key))
            return null;

        PrepareContent(content, window);

        var dialog = new CustomDialog
        {
            Content = content,
            Title = null
        };
        var session = new DialogSession(key, window, dialog);
        OpenSessions[key] = session;
        CommandManager.InvalidateRequerySuggested();

        try
        {
            await window.ShowMetroDialogAsync(dialog);
            session.Shown.TrySetResult(true);
        }
        catch
        {
            OpenSessions.Remove(key);
            dialog.Content = null;
            session.Shown.TrySetResult(false);
            session.Completion.TrySetResult(null);
            throw;
        }

        return await session.Completion.Task;
    }

    private static async Task HideAsync(DialogSession session, object? result)
    {
        if (!session.Window.Dispatcher.CheckAccess())
        {
            await session.Window.Dispatcher.InvokeAsync(() => HideAsync(session, result)).Task.Unwrap();
            return;
        }

        try
        {
            if (await session.Shown.Task && session.Window.IsLoaded)
                await session.Window.HideMetroDialogAsync(session.Dialog);
        }
        catch
        {
            // 窗口已关闭或对话框尚未进入可视树时，仍然要完成等待方。
        }
        finally
        {
            if (OpenSessions.TryGetValue(session.Key, out var current) && ReferenceEquals(current, session))
                OpenSessions.Remove(session.Key);
            session.Dialog.Content = null;
            session.Completion.TrySetResult(result);
            CommandManager.InvalidateRequerySuggested();
        }
    }

    private static async Task EnsureLoadedAsync(MetroWindow window)
    {
        if (window.IsLoaded)
            return;

        var loaded = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        RoutedEventHandler handler = null!;
        handler = (_, _) =>
        {
            window.Loaded -= handler;
            loaded.TrySetResult(true);
        };
        window.Loaded += handler;
        if (window.IsLoaded)
        {
            window.Loaded -= handler;
            loaded.TrySetResult(true);
        }

        await loaded.Task;
    }

    private static void PrepareContent(object content, MetroWindow window)
    {
        if (content is not FrameworkElement element)
            return;

        Detach(element);
        if (element.DataContext == null)
            element.DataContext = window.DataContext;
    }

    private static void Detach(FrameworkElement element)
    {
        for (var attempt = 0; attempt < 8 && element.Parent != null; attempt++)
        {
            switch (element.Parent)
            {
                case ContentControl contentControl when ReferenceEquals(contentControl.Content, element):
                    contentControl.Content = null;
                    break;
                case ContentPresenter presenter when ReferenceEquals(presenter.Content, element):
                    presenter.Content = null;
                    break;
                case Decorator decorator when ReferenceEquals(decorator.Child, element):
                    decorator.Child = null;
                    break;
                case Panel panel when panel.Children.Contains(element):
                    panel.Children.Remove(element);
                    break;
                case ItemsControl items when items.Items.Contains(element):
                    items.Items.Remove(element);
                    break;
                default:
                    return;
            }
        }
    }

    private static void OnIdentifierChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not MetroWindow window)
            return;

        var identifier = Normalize(e.NewValue);
        if (identifier == null)
        {
            Unregister(window);
            return;
        }

        Register(window, identifier);
    }

    private static void Register(MetroWindow window, string identifier)
    {
        if (WindowIdentifiers.TryGetValue(window, out var previous) && previous != identifier
            && Windows.TryGetValue(previous, out var previousWindow) && ReferenceEquals(previousWindow, window))
        {
            Windows.Remove(previous);
        }

        if (Windows.TryGetValue(identifier, out var existing) && !ReferenceEquals(existing, window))
        {
            if (WindowIdentifiers.TryGetValue(existing, out var existingId) && existingId == identifier)
                WindowIdentifiers.Remove(existing);
            existing.Closed -= OnWindowClosed;
        }

        Windows[identifier] = window;
        WindowIdentifiers[window] = identifier;
        window.Closed -= OnWindowClosed;
        window.Closed += OnWindowClosed;
        CommandManager.InvalidateRequerySuggested();
    }

    private static void Unregister(MetroWindow window)
    {
        window.Closed -= OnWindowClosed;
        if (!WindowIdentifiers.TryGetValue(window, out var identifier))
            return;

        WindowIdentifiers.Remove(window);
        if (Windows.TryGetValue(identifier, out var registered) && ReferenceEquals(registered, window))
            Windows.Remove(identifier);
    }

    private static void OnWindowClosed(object? sender, EventArgs e)
    {
        if (sender is not MetroWindow window)
            return;

        if (WindowIdentifiers.TryGetValue(window, out var identifier)
            && OpenSessions.TryGetValue(identifier, out var session))
        {
            session.Shown.TrySetResult(false);
            session.Completion.TrySetResult(null);
            session.Dialog.Content = null;
            OpenSessions.Remove(identifier);
        }

        Unregister(window);
    }

    private static void OnCloseExecuted(object sender, ExecutedRoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is not DependencyObject source || !TryGetKey(source, out var key))
            return;
        Close(key, e.Parameter);
    }

    private static void OnCloseCanExecute(object sender, CanExecuteRoutedEventArgs e)
    {
        e.Handled = true;
        e.CanExecute = sender is DependencyObject source && TryGetKey(source, out var key) && OpenSessions.ContainsKey(key);
    }

    private static void OnOpenExecuted(object sender, ExecutedRoutedEventArgs e)
    {
        e.Handled = true;
        if (e.Parameter is null || sender is not DependencyObject source || !TryGetKey(source, out var key))
            return;
        if (OpenSessions.ContainsKey(key))
            return;
        _ = ShowAsync(e.Parameter, key);
    }

    private static void OnOpenCanExecute(object sender, CanExecuteRoutedEventArgs e)
    {
        e.Handled = true;
        e.CanExecute = e.Parameter != null
                       && sender is DependencyObject source
                       && TryGetKey(source, out var key)
                       && !OpenSessions.ContainsKey(key);
    }

    private static bool TryGetKey(DependencyObject source, out string key)
    {
        key = "";
        var window = Window.GetWindow(source) as MetroWindow;
        if (window == null || !WindowIdentifiers.TryGetValue(window, out var identifier))
            return false;
        key = identifier;
        return true;
    }

    private static string Key(string? identifier) => identifier ?? "";

    private static string? Normalize(object? identifier) => identifier switch
    {
        null => null,
        string text => text,
        _ => identifier.ToString()
    };

    private sealed class DialogSession(string key, MetroWindow window, CustomDialog dialog)
    {
        public string Key { get; } = key;
        public MetroWindow Window { get; } = window;
        public CustomDialog Dialog { get; } = dialog;
        public TaskCompletionSource<bool> Shown { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<object?> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool CloseStarted { get; set; }
    }
}
