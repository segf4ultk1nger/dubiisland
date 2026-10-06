using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ClassIsland.Core.Abstractions.Services;
using ClassIsland.Core.Abstractions.Services.Management;
using ClassIsland.Shared;
using ClassIsland.Shared.Models.Profile;
using ClassIsland.Core.Services;
using MahApps.Metro.Controls.Dialogs;
using ClassIsland.Views;

namespace ClassIsland.Controls;

/// <summary>
/// ClassPlanGroupItemControl.xaml 的交互逻辑
/// </summary>
public sealed partial class ClassPlanGroupItemControl : UserControl, INotifyPropertyChanged
{
    public static readonly DependencyProperty ItemProperty = DependencyProperty.Register(
        nameof(Item), typeof(ClassPlanGroup), typeof(ClassPlanGroupItemControl), new PropertyMetadata(new ClassPlanGroup()));

    public ClassPlanGroup Item
    {
        get { return (ClassPlanGroup)GetValue(ItemProperty); }
        set { SetValue(ItemProperty, value); }
    }

    public static readonly DependencyProperty KeyProperty = DependencyProperty.Register(
        nameof(Key), typeof(Guid), typeof(ClassPlanGroupItemControl), new PropertyMetadata(Guid.Empty, (o, args) =>
        {
            if (o is not ClassPlanGroupItemControl control) 
                return;
            var key = control.Key;
            var policy = IAppHost.GetService<IManagementService>().Policy;
            control.IsProtected = key == ClassPlanGroup.DefaultGroupGuid ||
                                  key == ClassPlanGroup.GlobalGroupGuid ||
                                  policy.DisableProfileEditing ||
                                  policy.DisableProfileClassPlanEditing;
        }));

    private bool _isRenaming = false;

    public Guid Key
    {
        get { return (Guid)GetValue(KeyProperty); }
        set { SetValue(KeyProperty, value); }
    }

    private IProfileService ProfileService { get; } = App.GetService<IProfileService>();

    private readonly string _parentDialogId;
    private bool _isProtected = false;

    public bool IsProtected
    {
        get => _isProtected;
        set
        {
            if (value == _isProtected) return;
            _isProtected = value;
            OnPropertyChanged();
        }
    }

    public bool IsRenaming
    {
        get => _isRenaming;
        set
        {
            if (value == _isRenaming) return;
            _isRenaming = value;
            OnPropertyChanged();
        }
    }

    public ClassPlanGroupItemControl()
    {
        InitializeComponent();
        _parentDialogId = App.GetService<ProfileSettingsWindow>().ViewModel.DialogHostId.ToString();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void MenuItemRename_OnClick(object sender, RoutedEventArgs e)
    {
        IsRenaming = true;
        MainTextBox.Focus();
        MainTextBox.SelectAll();
    }

    private void TextBox_OnLostFocus(object sender, RoutedEventArgs e)
    {
        IsRenaming = false;
    }

    private void TextBox_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is System.Windows.Input.Key.Enter or System.Windows.Input.Key.Escape)
        {
            IsRenaming = false;
            Focus();
        }
    }

    private void MenuItemTemp_OnClick(object sender, RoutedEventArgs e)
    {
        ProfileService.SetupTempClassPlanGroup(Key);
    }

    private async void MenuItemDisband_OnClick(object sender, RoutedEventArgs e)
    {
        var r = await DialogService.ShowMessageAsync(_parentDialogId, "解散课表群",
            "你确定要解散这个课表群吗？解散后，课表群内的课表将被移动到默认课表群中。",
            MessageDialogStyle.AffirmativeAndNegative,
            new MetroDialogSettings { AffirmativeButtonText = "解散", NegativeButtonText = "取消" });
        if (r != MessageDialogResult.Affirmative)
        {
            return;
        }

        ProfileService.Profile.DisbandClassPlanGroup(Key);
    }

    private async void MenuItemDelete_OnClick(object sender, RoutedEventArgs e)
    {
        var r = await DialogService.ShowMessageAsync(_parentDialogId, "删除课表群",
            "你确定要删除这个课表群吗？删除后，此课表群内的课表也将全部删除。这个操作无法撤销！\n" +
            "如果您只是想移除课表群而保留课表，请解散课表群。",
            MessageDialogStyle.AffirmativeAndNegative,
            new MetroDialogSettings { AffirmativeButtonText = "删除", NegativeButtonText = "取消" });
        if (r != MessageDialogResult.Affirmative)
        {
            return;
        }

        ProfileService.Profile.DeleteClassPlanGroup(Key);
    }
}