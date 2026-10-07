using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using ClassIsland.Services;
using ClassIsland.EdgeTts;
using ClassIsland.EdgeTts.Model;

namespace ClassIsland.Controls.SpeechProviderSettingsControls;

/// <summary>
/// EdgeTtsSpeechServiceSettingsControl.xaml 的交互逻辑
/// </summary>
public partial class EdgeTtsSpeechServiceSettingsControl
{
    public SettingsService SettingsService { get; }

    public List<eVoice> EdgeVoices { get; } =
        EdgeTtsClient.GetVoice().FindAll(i => i.Locale.Contains("zh-CN"));

    public EdgeTtsSpeechServiceSettingsControl(SettingsService settingsService)
    {
        SettingsService = settingsService;
        InitializeComponent();
    }
}