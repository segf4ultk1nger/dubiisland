using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using ClassIsland.Core.Abstractions.Services;
using ClassIsland.Core.Abstractions.Services.Management;
using ClassIsland.Core.Controls;
using ClassIsland.Shared.Models.Profile;
using ClassIsland.Services;
using ClassIsland.Core.Services;
using ClassIsland.ViewModels;
using MahApps.Metro.Controls.Dialogs;

namespace ClassIsland.Views;

/// <summary>
/// ClassChangingWindow.xaml 的交互逻辑
/// </summary>
public partial class ClassChangingWindow : MyWindow
{
    public ClassChangingViewModel ViewModel { get; } = new();

    public SettingsService SettingsService { get; } = App.GetService<SettingsService>();

    public IProfileService ProfileService { get; } = App.GetService<IProfileService>();

    public IManagementService ManagementService { get; } = App.GetService<IManagementService>();

    public static readonly DependencyProperty ClassPlanProperty = DependencyProperty.Register(
        nameof(ClassPlan), typeof(ClassPlan), typeof(ClassChangingWindow), new PropertyMetadata(default(ClassPlan)));

    public ClassPlan ClassPlan
    {
        get { return (ClassPlan)GetValue(ClassPlanProperty); }
        set { SetValue(ClassPlanProperty, value); }
    }
    private int GetSubjectIndex(int index)
    {
        var k = ClassPlan?.TimeLayout.Layouts[index];
        var l = (from t in ClassPlan?.TimeLayout.Layouts where t.TimeType == 0 select t).ToList();
        var i = l.IndexOf(k);
        return i;
    }

    public ClassChangingWindow()
    {
        //DataContext = this;
        InitializeComponent();
    }

    private void Selector_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ViewModel.IsAutoNextStep || e.OriginalSource.GetType() == typeof(TabControl))
            return;
        ViewModel.IsAutoNextStep = true;
        ViewModel.SlideIndex = 1;
    }

    private void ButtonPrev_OnClick(object sender, RoutedEventArgs e)
    {
        ViewModel.SlideIndex = 0;
    }

    private void ButtonNext_OnClick(object sender, RoutedEventArgs e)
    {
        ViewModel.SlideIndex = 1;
    }

    private async void ButtonConfirmClassChanging_OnClick(object sender, RoutedEventArgs e)
    {
        var l = ProfileService.Profile.ClassPlans.Where(i => i.Value == ClassPlan).ToList();
        if (l.Count <= 0)
        {
            return;
        }
        var key = (Guid?)l[0].Key;
        if (!ViewModel.WriteToSourceClassPlan && !ClassPlan.IsOverlay && ProfileService.Profile.OverlayClassPlanId != null)
        {
            var r = await DialogService.ShowMessageAsync(ViewModel.DialogIdentifier.ToString(),
                "覆盖当前的临时层", "当前已经存在一个临时层课表，如果继续换课，那么该临时层将被覆盖。是否继续？",
                MessageDialogStyle.AffirmativeAndNegative,
                new MetroDialogSettings { AffirmativeButtonText = "继续", NegativeButtonText = "取消" });
            if (r != MessageDialogResult.Affirmative)
            {
                return;
            }
            ProfileService.ClearTempClassPlan();
        }

        if (!ViewModel.WriteToSourceClassPlan && !ClassPlan.IsOverlay)
        {
            key = ProfileService.CreateTempClassPlan(key.Value);
        }

        if (key == null)
        {
            return;
        }
        var cp = ProfileService.Profile.ClassPlans[key.Value];
        var aI = GetSubjectIndex(ViewModel.SourceIndex);
        var bI = 0;
        Guid a;
        Guid b;

        if (SettingsService.Settings.IsSwapMode)
        {
            bI = GetSubjectIndex(ViewModel.SwapModeTargetIndex);
            a = cp.Classes[aI].SubjectId;
            b = cp.Classes[bI].SubjectId;
            cp.Classes[aI].SubjectId = b;
            cp.Classes[bI].SubjectId = a;
        }
        else
        {
            if (ViewModel.TargetSubjectIndex == Guid.Empty)
            {
                return;
            }

            cp.Classes[aI].SubjectId = ViewModel.TargetSubjectIndex;
        }

        ProfileService.SaveProfile();
        Close();
    }

    protected override void OnContentRendered(EventArgs e)
    {
        DataContext = this;
        base.OnContentRendered(e);
    }

    private void ButtonTemporaryClassPlan_OnClick(object sender, RoutedEventArgs e)
    {
        App.GetService<ProfileSettingsWindow>().OpenDrawer("TemporaryClassPlan");
        App.GetService<ProfileSettingsWindow>().Open();
        Close();
    }
}