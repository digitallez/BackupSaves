using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace BackupSaves.Core.Services;

public static class ProcessWatchService
{
    private static readonly ConcurrentDictionary<string, Regex> RegexCache =
        new(StringComparer.OrdinalIgnoreCase);

    private static readonly ConcurrentDictionary<string, MatchCacheEntry> MatchCache =
        new(StringComparer.OrdinalIgnoreCase);

    private sealed class MatchCacheEntry
    {
        public int? Pid;
        public bool Running;
        public DateTimeOffset LastFullScanUtc;
    }

    public static bool IsMatchPatternConfigured(string? pattern) =>
        !string.IsNullOrWhiteSpace(pattern);

    /// <summary>
    /// Cheap check: prefers cached PID, then GetProcessesByName / ProcessName only.
    /// Never touches MainModule while scanning all processes (avoids Win32Exception spam + UI stalls).
    /// </summary>
    /// <param name="minIntervalBetweenFullScans">
    /// When the process is not known-running, skip a full enumeration if the last full scan
    /// was within this interval. PID liveness is always checked (detects exit promptly).
    /// Pass null to always allow a full scan when needed (e.g. at backup time).
    /// </param>
    public static bool IsAnyMatchingProcessRunning(string? pattern, TimeSpan? minIntervalBetweenFullScans = null)
    {
        if (!IsMatchPatternConfigured(pattern))
            return false;

        var key = NormalizeKey(pattern!);
        if (TryFastPath(key, minIntervalBetweenFullScans, out var cached))
            return cached;

        return RunFullScan(key, pattern!.Trim());
    }

    /// <summary>
    /// Evaluate many patterns with one process snapshot when any need a wildcard scan.
    /// Respects per-pattern min intervals and shared PID cache.
    /// </summary>
    public static IReadOnlyDictionary<string, bool> EvaluatePatterns(
        IEnumerable<(string Pattern, TimeSpan? MinInterval)> requests)
    {
        var results = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        var needSnapshot = new List<string>();
        var needSnapshotKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (pattern, minInterval) in requests)
        {
            if (!IsMatchPatternConfigured(pattern))
                continue;

            var key = NormalizeKey(pattern);
            if (results.ContainsKey(key))
                continue;

            if (TryFastPath(key, minInterval, out var cached))
            {
                results[key] = cached;
                continue;
            }

            var trimmed = pattern.Trim();
            if (CanUseByNameOnly(trimmed))
            {
                results[key] = RunFullScan(key, trimmed);
                continue;
            }

            if (needSnapshotKeys.Add(key))
                needSnapshot.Add(trimmed);
        }

        if (needSnapshot.Count > 0)
        {
            Process[] procs;
            try
            {
                procs = Process.GetProcesses();
            }
            catch
            {
                foreach (var trimmed in needSnapshot)
                {
                    var key = NormalizeKey(trimmed);
                    if (!results.ContainsKey(key))
                        results[key] = false;
                    StoreCache(key, running: false, pid: null);
                }

                return results;
            }

            try
            {
                foreach (var trimmed in needSnapshot)
                {
                    var key = NormalizeKey(trimmed);
                    if (results.ContainsKey(key))
                        continue;

                    var running = MatchAgainstSnapshot(procs, trimmed, out var pid);
                    results[key] = running;
                    StoreCache(key, running, pid);
                }
            }
            finally
            {
                foreach (var p in procs)
                    p.Dispose();
            }
        }

        return results;
    }

    public static bool MatchesProcess(Process proc, string pattern)
    {
        if (MatchesProcessName(proc.ProcessName, pattern))
            return true;

        // Path patterns only: probe MainModule for this one process (not while scanning all)
        if (!LooksLikePath(pattern))
            return false;

        try
        {
            var fullPath = proc.MainModule?.FileName;
            if (string.IsNullOrWhiteSpace(fullPath))
                return false;

            if (MatchesWildcard(pattern, fullPath))
                return true;

            var fileName = Path.GetFileName(fullPath);
            return MatchesWildcard(pattern, fileName)
                   || MatchesWildcard(pattern, Path.GetFileNameWithoutExtension(fileName));
        }
        catch
        {
            return false;
        }
    }

    public static bool MatchesWildcard(string pattern, string value)
    {
        if (string.IsNullOrEmpty(value))
            return false;

        if (!ContainsWildcards(pattern))
            return string.Equals(pattern, value, StringComparison.OrdinalIgnoreCase)
                   || string.Equals(pattern, Path.GetFileName(value), StringComparison.OrdinalIgnoreCase);

        var regex = RegexCache.GetOrAdd(pattern, static p =>
        {
            var escaped = "^" + Regex.Escape(p)
                .Replace("\\*", ".*", StringComparison.Ordinal)
                .Replace("\\?", ".", StringComparison.Ordinal) + "$";
            return new Regex(escaped, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
        });

        return regex.IsMatch(value);
    }

    public sealed record RunningProcessInfo(int Pid, string Name, string? Path);

    /// <summary>List for the process picker. Uses ProcessName only (no MainModule → no Win32 spam).</summary>
    public static IReadOnlyList<RunningProcessInfo> ListDistinctRunning()
    {
        var list = new List<RunningProcessInfo>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var proc in Process.GetProcesses().OrderBy(p => p.ProcessName, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var name = proc.ProcessName + ".exe";
                if (!seen.Add(name))
                    continue;

                list.Add(new RunningProcessInfo(proc.Id, name, Path: null));
            }
            catch
            {
                // ignore
            }
            finally
            {
                proc.Dispose();
            }
        }

        return list;
    }

    /// <summary>Drop cached state for a pattern (e.g. after profile edit).</summary>
    public static void Invalidate(string? pattern)
    {
        if (!IsMatchPatternConfigured(pattern))
            return;
        MatchCache.TryRemove(NormalizeKey(pattern!), out _);
    }

    private static bool TryFastPath(string key, TimeSpan? minIntervalBetweenFullScans, out bool running)
    {
        running = false;
        if (!MatchCache.TryGetValue(key, out var entry))
            return false;

        if (entry.Pid is int pid)
        {
            if (IsProcessAlive(pid))
            {
                running = true;
                return true;
            }

            // Known process exited — clear PID and force a full scan (another instance may exist).
            MatchCache.AddOrUpdate(
                key,
                _ => new MatchCacheEntry { Running = false, Pid = null, LastFullScanUtc = entry.LastFullScanUtc },
                (_, existing) =>
                {
                    existing.Running = false;
                    existing.Pid = null;
                    return existing;
                });
            return false;
        }

        if (entry.Running)
            return false; // inconsistent: running without PID → rescan

        if (minIntervalBetweenFullScans is TimeSpan min
            && min > TimeSpan.Zero
            && DateTimeOffset.UtcNow - entry.LastFullScanUtc < min)
        {
            running = false;
            return true;
        }

        return false;
    }

    private static bool RunFullScan(string key, string trimmed)
    {
        // notepad.exe / notepad / C:\Games\game.exe → GetProcessesByName
        if (!ContainsWildcards(trimmed))
        {
            var baseName = Path.GetFileNameWithoutExtension(trimmed);
            if (string.IsNullOrEmpty(baseName))
            {
                StoreCache(key, running: false, pid: null);
                return false;
            }

            var running = TryFindByName(baseName, out var pid);
            StoreCache(key, running, pid);
            return running;
        }

        // Wildcards: enumerate once
        var matched = TryFindByWildcard(trimmed, out var wildPid);
        StoreCache(key, matched, wildPid);
        return matched;
    }

    private static bool CanUseByNameOnly(string trimmed) =>
        !ContainsWildcards(trimmed);

    private static bool MatchAgainstSnapshot(Process[] procs, string pattern, out int? pid)
    {
        pid = null;
        var filePart = LooksLikePath(pattern) ? Path.GetFileName(pattern) : pattern;

        foreach (var proc in procs)
        {
            try
            {
                if (MatchesProcessName(proc.ProcessName, pattern)
                    || (!string.IsNullOrEmpty(filePart) && MatchesProcessName(proc.ProcessName, filePart)))
                {
                    pid = proc.Id;
                    return true;
                }
            }
            catch
            {
                // ignore
            }
        }

        return false;
    }

    private static bool TryFindByName(string baseName, out int? pid)
    {
        pid = null;
        Process[] procs;
        try
        {
            procs = Process.GetProcessesByName(baseName);
        }
        catch
        {
            return false;
        }

        try
        {
            if (procs.Length == 0)
                return false;

            pid = procs[0].Id;
            return true;
        }
        finally
        {
            foreach (var p in procs)
                p.Dispose();
        }
    }

    private static bool TryFindByWildcard(string pattern, out int? pid)
    {
        pid = null;
        Process[] procs;
        try
        {
            procs = Process.GetProcesses();
        }
        catch
        {
            return false;
        }

        try
        {
            return MatchAgainstSnapshot(procs, pattern, out pid);
        }
        finally
        {
            foreach (var p in procs)
                p.Dispose();
        }
    }

    private static bool IsProcessAlive(int pid)
    {
        try
        {
            Process.GetProcessById(pid).Dispose();
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch
        {
            return false;
        }
    }

    private static void StoreCache(string key, bool running, int? pid)
    {
        MatchCache.AddOrUpdate(
            key,
            _ => new MatchCacheEntry
            {
                Running = running,
                Pid = running ? pid : null,
                LastFullScanUtc = DateTimeOffset.UtcNow
            },
            (_, existing) =>
            {
                existing.Running = running;
                existing.Pid = running ? pid : null;
                existing.LastFullScanUtc = DateTimeOffset.UtcNow;
                return existing;
            });
    }

    private static string NormalizeKey(string pattern) => pattern.Trim();

    private static bool MatchesProcessName(string processName, string pattern)
    {
        return MatchesWildcard(pattern, processName)
               || MatchesWildcard(pattern, processName + ".exe");
    }

    private static bool ContainsWildcards(string pattern) =>
        pattern.Contains('*') || pattern.Contains('?');

    private static bool LooksLikePath(string pattern) =>
        pattern.Contains('\\') || pattern.Contains('/') || pattern.Contains(':');
}
