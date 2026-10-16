namespace BackupSaves.Core;

/// <summary>Manual pause/resume gate for long-running jobs (wait between units of work).</summary>
public sealed class PauseGate
{
    private readonly ManualResetEventSlim _gate = new(initialState: true);

    public bool IsPaused { get; private set; }

    public void Pause()
    {
        IsPaused = true;
        _gate.Reset();
    }

    public void Resume()
    {
        IsPaused = false;
        _gate.Set();
    }

    public void WaitIfPaused(CancellationToken ct = default) => _gate.Wait(ct);
}
