namespace BackupSaves.Core.Models;

public enum BackupProgressPhase
{
    Preparing,
    Staging,
    Checksum,
    Compressing,
    Finalizing
}

/// <summary>Approximate backup progress. <see cref="Fraction"/> is 0..1.</summary>
public readonly record struct BackupProgress(
    double Fraction,
    BackupProgressPhase Phase,
    long BytesDone = 0,
    long BytesTotal = 0);
