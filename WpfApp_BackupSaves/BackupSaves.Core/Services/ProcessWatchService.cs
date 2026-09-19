using System.Diagnostics;
using System.Text.RegularExpressions;

namespace BackupSaves.Core.Services;

public static class ProcessWatchService
{
    public static bool IsMatchPatternConfigured(string? pattern) =>
        !string.IsNullOrWhiteSpace(pattern);

    public static bool IsAnyMatchingProcessRunning(string? pattern)
    {
        if (!IsMatchPatternConfigured(pattern))
            return false;

        var trimmed = pattern!.Trim();
        foreach (var proc in Process.GetProcesses())
        {
            try
            {
                if (MatchesProcess(proc, trimmed))
                    return true;
            }
            catch
            {
                // access denied / exited
            }
            finally
            {
                proc.Dispose();
            }
        }

        return false;
    }

    public static bool MatchesProcess(Process proc, string pattern)
    {
        var name = proc.ProcessName;
        if (MatchesWildcard(pattern, name) || MatchesWildcard(pattern, name + ".exe"))
            return true;

        string? fileName = null;
        string? fullPath = null;
        try
        {
            fullPath = proc.MainModule?.FileName;
            if (!string.IsNullOrWhiteSpace(fullPath))
                fileName = Path.GetFileName(fullPath);
        }
        catch
        {
            // MainModule often throws for elevated/system processes
        }

        if (!string.IsNullOrWhiteSpace(fileName)
            && (MatchesWildcard(pattern, fileName) || MatchesWildcard(pattern, Path.GetFileNameWithoutExtension(fileName))))
            return true;

        if (!string.IsNullOrWhiteSpace(fullPath) && MatchesWildcard(pattern, fullPath))
            return true;

        return false;
    }

    public static bool MatchesWildcard(string pattern, string value)
    {
        if (string.IsNullOrEmpty(value))
            return false;

        // Exact (case-insensitive) if no wildcards
        if (!pattern.Contains('*') && !pattern.Contains('?'))
            return string.Equals(pattern, value, StringComparison.OrdinalIgnoreCase)
                   || string.Equals(pattern, Path.GetFileName(value), StringComparison.OrdinalIgnoreCase);

        var regex = "^" + Regex.Escape(pattern)
            .Replace("\\*", ".*", StringComparison.Ordinal)
            .Replace("\\?", ".", StringComparison.Ordinal) + "$";
        return Regex.IsMatch(value, regex, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    public sealed record RunningProcessInfo(int Pid, string Name, string? Path);

    public static IReadOnlyList<RunningProcessInfo> ListDistinctRunning()
    {
        var list = new List<RunningProcessInfo>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var proc in Process.GetProcesses().OrderBy(p => p.ProcessName, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                string? path = null;
                try { path = proc.MainModule?.FileName; } catch { /* ignore */ }

                var key = path ?? (proc.ProcessName + ".exe");
                if (!seen.Add(key))
                    continue;

                list.Add(new RunningProcessInfo(proc.Id, proc.ProcessName + ".exe", path));
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
}
