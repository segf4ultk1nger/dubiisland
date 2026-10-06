using System;
using ClassIsland.Core.Abstractions.Services;

namespace ClassIsland.Services;

/// <inheritdoc />
public class ConditionPulseService : IConditionPulseService
{
    /// <inheritdoc />
    public event EventHandler? StatusUpdated;

    /// <inheritdoc />
    public void NotifyStatusChanged() => StatusUpdated?.Invoke(this, EventArgs.Empty);
}
