namespace CrmSystem.Services;

/// <summary>
/// Lets controllers wake the reminder engine immediately (for example after a
/// follow-up is created less than 24 hours before its due time) instead of
/// waiting for the next periodic scan.
/// </summary>
public sealed class ReminderSignal
{
    private readonly SemaphoreSlim _signal = new(0, 1);

    public void Wake()
    {
        if (_signal.CurrentCount == 0)
        {
            try { _signal.Release(); } catch (SemaphoreFullException) { /* already signalled */ }
        }
    }

    /// <summary>Waits until woken or until the timeout elapses.</summary>
    public Task WaitAsync(TimeSpan timeout, CancellationToken ct) => _signal.WaitAsync(timeout, ct);
}
