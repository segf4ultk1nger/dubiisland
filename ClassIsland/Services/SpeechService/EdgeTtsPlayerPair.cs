using System.Threading;

using ClassIsland.EdgeTts.Utils;

namespace ClassIsland.Services.SpeechService;

public class EdgeTtsPlayerPair(AudioPlayer player, CancellationTokenSource tokenSource)
{
    public AudioPlayer Player { get; set; } = player;

    public CancellationTokenSource CancellationTokenSource { get; set; } = tokenSource;
}