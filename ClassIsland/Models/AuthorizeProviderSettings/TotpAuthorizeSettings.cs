using CommunityToolkit.Mvvm.ComponentModel;

namespace ClassIsland.Models.AuthorizeProviderSettings;

public partial class TotpAuthorizeSettings : ObservableObject
{
    [ObservableProperty]
    private string _secret = "";
}
