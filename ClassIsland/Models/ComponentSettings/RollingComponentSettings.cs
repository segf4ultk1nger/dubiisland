using System.Collections.ObjectModel;
using ClassIsland.Core.Abstractions.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ClassIsland.Models.ComponentSettings;

public partial class RollingComponentSettings : ObservableObject, IComponentContainerSettings
{
    [ObservableProperty] private ObservableCollection<Core.Models.Components.ComponentSettings> _children = [];

    [ObservableProperty] private double _speedPixelPerSecond = 40.0;

    [ObservableProperty] private bool _isPauseEnabled = true;

    [ObservableProperty] private double _pauseOffsetX = 0.0;

    [ObservableProperty] private double _pauseSeconds = 10.0;

    [ObservableProperty] private bool _pauseOnRule = false;

    [ObservableProperty] private string _pauseCondition = "";

    [ObservableProperty] private bool _stopOnRule = false;

    [ObservableProperty] private string _stopCondition = "";
}