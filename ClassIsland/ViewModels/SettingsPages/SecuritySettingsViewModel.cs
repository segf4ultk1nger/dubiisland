using CommunityToolkit.Mvvm.ComponentModel;

namespace ClassIsland.ViewModels.SettingsPages;

public partial class SecuritySettingsViewModel : ObservableObject
{
    [ObservableProperty] private bool _isLocked = true;
}
