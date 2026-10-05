using System;

namespace ClassIsland.Services;

public interface INotificationVisualHost
{
    void OnNotificationTopmostChanged();
    void OnNotificationEffectRequested();
    void OnNotificationProgressStarted(TimeSpan duration);
    void OnNotificationProgressStopped();
}
