using BackupSaves.Core.Models;

namespace BackupSaves.Core.Services;

/// <summary>Maps stage-local byte progress into a weighted overall 0..1 fraction.</summary>
internal sealed class BackupProgressTracker
{
    private readonly IProgress<BackupProgress>? _progress;
    private double _lastReported = -1;

    public BackupProgressTracker(IProgress<BackupProgress>? progress)
    {
        _progress = progress;
    }

    public void Report(BackupProgressPhase phase, double fraction, long bytesDone = 0, long bytesTotal = 0)
    {
        if (_progress is null)
            return;

        fraction = Math.Clamp(fraction, 0, 1);
        if (fraction - _lastReported < 0.005 && fraction < 0.999 && phase != BackupProgressPhase.Finalizing)
            return;

        _lastReported = fraction;
        _progress.Report(new BackupProgress(fraction, phase, bytesDone, bytesTotal));
    }

    public void ReportPhase(
        BackupProgressPhase phase,
        double phaseStart,
        double phaseEnd,
        long bytesDone,
        long bytesTotal)
    {
        var local = bytesTotal <= 0 ? 1.0 : Math.Clamp((double)bytesDone / bytesTotal, 0, 1);
        var overall = phaseStart + (phaseEnd - phaseStart) * local;
        Report(phase, overall, bytesDone, bytesTotal);
    }
}
