using System.Diagnostics;
using System.Globalization;
using System.Security.Principal;

namespace Sweep.Games;

/// <summary>Frame-time numbers for one measurement window.</summary>
public sealed class Capture
{
    public int Frames { get; init; }
    public double AvgFps { get; init; }
    public double MedianMs { get; init; }
    public double P99Ms { get; init; }
    public double StuttersPerMin { get; init; }
    public string PresentMode { get; init; } = "unknown";
    public List<string> TopCpu { get; set; } = new();

    /// <summary>A stutter is a frame much slower than the typical one.</summary>
    public bool IsProblem => StuttersPerMin >= 3 || P99Ms > 2.0 * MedianMs;

    public string Summary =>
        $"{AvgFps:F0} FPS average, 1% low {1000.0 / P99Ms:F0} FPS, {StuttersPerMin:F1} stutters per minute";
}

public static class FrameStats
{
    /// <summary>Reads a PresentMon CSV. Returns null when there are too few frames to judge.</summary>
    public static Capture? Parse(string csvPath)
    {
        if (!File.Exists(csvPath)) return null;
        var lines = File.ReadAllLines(csvPath);
        if (lines.Length < 2) return null;

        var header = lines[0].Split(',');
        int Idx(params string[] names)
        {
            foreach (var n in names)
            {
                int i = Array.FindIndex(header, h => h.Trim().Equals(n, StringComparison.OrdinalIgnoreCase));
                if (i >= 0) return i;
            }
            return -1;
        }

        // PresentMon 2.x calls it FrameTime; 1.x called it msBetweenPresents.
        int frameCol = Idx("FrameTime", "msBetweenPresents");
        if (frameCol < 0)
            throw new InvalidDataException("No frame time column. Columns: " + string.Join(", ", header.Take(12)));
        int modeCol = Idx("PresentMode");

        var times = new List<double>();
        var modes = new Dictionary<string, int>();
        for (int i = 1; i < lines.Length; i++)
        {
            var f = lines[i].Split(',');
            if (f.Length <= frameCol) continue;
            if (!double.TryParse(f[frameCol], NumberStyles.Float, CultureInfo.InvariantCulture, out var ms) || ms <= 0)
                continue;
            times.Add(ms);
            if (modeCol >= 0 && f.Length > modeCol)
                modes[f[modeCol]] = modes.GetValueOrDefault(f[modeCol]) + 1;
        }
        if (times.Count < 100) return null;

        var sorted = times.OrderBy(x => x).ToList();
        double median = sorted[sorted.Count / 2];
        double p99 = sorted[(int)(0.99 * (sorted.Count - 1))];
        double totalMs = times.Sum();
        double threshold = Math.Max(2.0 * median, median + 10);
        int stutters = times.Count(t => t > threshold);

        return new Capture
        {
            Frames = times.Count,
            AvgFps = 1000.0 / (totalMs / times.Count),
            MedianMs = median,
            P99Ms = p99,
            StuttersPerMin = stutters / (totalMs / 60000.0),
            PresentMode = modes.Count > 0 ? modes.OrderByDescending(kv => kv.Value).First().Key : "unknown"
        };
    }
}

/// <summary>Plays along with the user: measures frame times with PresentMon (bundled, read-only,
/// never touches the game), then tries safe fixes one at a time and keeps only what helps.</summary>
public static class TestRun
{
    const int BaselineSeconds = 45;
    const int RoundSeconds = 40;

    static string PresentMonPath => Path.Combine(AppContext.BaseDirectory, "PresentMon.exe");

    static bool IsAdmin()
    {
        using var id = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
    }

    public static async Task RunAsync(string exePath, IProgress<string> log, CancellationToken ct)
    {
        if (!File.Exists(PresentMonPath))
        {
            log.Report("PresentMon.exe is missing next to Sweep.exe. Extract the whole release zip and keep the files together.");
            return;
        }
        if (!IsAdmin())
        {
            log.Report("Test Run needs administrator rights. Close Sweep, right-click Sweep.exe, choose Run as administrator, then try again.");
            return;
        }

        var exeName = Path.GetFileName(exePath);
        var procName = Path.GetFileNameWithoutExtension(exePath);
        if (Process.GetProcessesByName(procName).Length == 0)
        {
            log.Report($"{exeName} is not running. Start the game, load into a level, then click Test Run.");
            return;
        }

        log.Report($"Measuring {BaselineSeconds} seconds. Keep playing in the same area and keep moving.");
        var (baseline, error) = await Measure(exeName, procName, BaselineSeconds, ct);
        if (baseline == null)
        {
            log.Report(error ?? "No frames captured.");
            return;
        }

        log.Report($"Before: {baseline.Summary}. Present mode: {baseline.PresentMode}.");
        if (baseline.PresentMode.StartsWith("Composed", StringComparison.OrdinalIgnoreCase))
            log.Report("The game is drawn through the Windows desktop compositor (windowed). " +
                       "Fullscreen or borderless fullscreen usually lowers latency and tearing.");
        if (baseline.TopCpu.Count > 0)
            log.Report("Busiest apps during the test: " + string.Join(", ", baseline.TopCpu) + ".");

        if (!baseline.IsProblem)
        {
            log.Report("No stutter found in this window. If the lag shows up later, run Test Run again while it is happening.");
            return;
        }

        log.Report("Stutter found. Trying one fix at a time and keeping only what clearly helps.");

        var candidates = new List<IFix> { new PriorityFix(procName) };
        foreach (var f in Scanner.Scan(exePath)
                     .Where(x => x.IsProblem && x.Fix != null && x.LiveTestable && x.Risk == Risk.Safe))
            candidates.Add(f.Fix!);

        var kept = new List<string>();
        var current = baseline;
        List<UndoRecord>? pending = null;
        try
        {
            int round = 1;
            foreach (var fix in candidates)
            {
                ct.ThrowIfCancellationRequested();
                round++;

                var result = fix.Apply();
                if (!result.Ok)
                {
                    if (result.Undos.Count > 0) Journal.UndoNow(result.Undos);
                    log.Report($"Round {round}: skipped. {result.Message}");
                    continue;
                }

                pending = result.Undos;
                log.Report($"Round {round}: applied \"{fix.Label}\". Measuring {RoundSeconds} seconds.");
                var (after, err) = await Measure(exeName, procName, RoundSeconds, ct);
                if (after == null)
                {
                    Journal.UndoNow(pending);
                    pending = null;
                    log.Report((err ?? "No frames captured.") + " That change was undone.");
                    break;
                }

                if (Improved(current, after))
                {
                    Journal.Add(pending);
                    kept.Add(fix.Label);
                    current = after;
                    log.Report($"Kept: {fix.Label}. Now: {after.Summary}.");
                }
                else
                {
                    Journal.UndoNow(pending);
                    log.Report($"Undone, no clear gain: {fix.Label}. Measured: {after.Summary}.");
                }
                pending = null;
            }
        }
        finally
        {
            // Stopped or crashed mid-round: never leave an unmeasured change behind.
            if (pending != null) Journal.UndoNow(pending);
        }

        log.Report($"Result: before {baseline.Summary}. After {current.Summary}.");
        log.Report(kept.Count > 0
            ? "Kept: " + string.Join("; ", kept) + ". \"Undo all changes\" puts them back."
            : "No fix clearly helped. The cause is probably the game itself or your hardware, not a Windows setting.");
        log.Report("Not tested live (they need a game restart): use the Scan tab's Fix buttons for GPU preference and " +
                   "fullscreen optimizations, restart the game, then run Test Run again. " +
                   "Results vary with the scene, so play the same area each time.");
    }

    /// <summary>Clearly better: much lower worst frames, or fewer stutters without worse worst frames.</summary>
    static bool Improved(Capture before, Capture after) =>
        after.P99Ms <= before.P99Ms * 0.9 ||
        (after.StuttersPerMin <= before.StuttersPerMin * 0.75 && after.P99Ms <= before.P99Ms * 1.05);

    static async Task<(Capture? cap, string? error)> Measure(
        string exeName, string procName, int seconds, CancellationToken ct)
    {
        var csv = Path.Combine(Path.GetTempPath(), "sweep-capture.csv");
        try { if (File.Exists(csv)) File.Delete(csv); } catch { /* old file locked: PresentMon will overwrite */ }

        var psi = new ProcessStartInfo(PresentMonPath,
            $"--process_name \"{exeName}\" --output_file \"{csv}\" --timed {seconds} " +
            "--terminate_after_timed --stop_existing_session --no_console_stats")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        var cpuBefore = CpuSnapshot();
        using var p = Process.Start(psi);
        if (p == null) return (null, "Could not start PresentMon.");

        var outTask = p.StandardOutput.ReadToEndAsync();
        var errTask = p.StandardError.ReadToEndAsync();
        using var killOnCancel = ct.Register(() =>
        {
            try { if (!p.HasExited) p.Kill(true); } catch { /* already gone */ }
        });
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(seconds + 40));
        try
        {
            await p.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            try { if (!p.HasExited) p.Kill(true); } catch { /* already gone */ }
            ct.ThrowIfCancellationRequested();
            return (null, "PresentMon timed out.");
        }
        ct.ThrowIfCancellationRequested();

        var text = (await outTask) + (await errTask);
        Capture? cap;
        try
        {
            cap = FrameStats.Parse(csv);
        }
        catch (InvalidDataException ex)
        {
            return (null, ex.Message);
        }

        if (cap == null)
        {
            var detail = text.Trim();
            if (detail.Length > 300) detail = detail[..300];
            return (null, $"No frames captured (PresentMon exit code {p.ExitCode}). " +
                          "Play in a level, not a menu, and check the game is running. " + detail);
        }

        cap.TopCpu = TopCpuUsers(cpuBefore, CpuSnapshot(), seconds, procName);
        return (cap, null);
    }

    static Dictionary<int, (string name, double seconds)> CpuSnapshot()
    {
        var map = new Dictionary<int, (string name, double seconds)>();
        foreach (var p in Process.GetProcesses())
        {
            try { map[p.Id] = (p.ProcessName, p.TotalProcessorTime.TotalSeconds); }
            catch { /* protected process: skip */ }
            finally { p.Dispose(); }
        }
        return map;
    }

    static List<string> TopCpuUsers(
        Dictionary<int, (string name, double seconds)> before,
        Dictionary<int, (string name, double seconds)> after,
        int windowSeconds, string gameName)
    {
        var totals = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var (pid, now) in after)
        {
            if (!before.TryGetValue(pid, out var then) || then.name != now.name) continue;
            if (now.name.Equals(gameName, StringComparison.OrdinalIgnoreCase) ||
                Scanner.IgnoreProcesses.Contains(now.name)) continue;
            double pct = 100.0 * (now.seconds - then.seconds) / (windowSeconds * Environment.ProcessorCount);
            totals[now.name] = totals.GetValueOrDefault(now.name) + pct;
        }
        return totals.Where(kv => kv.Value >= 2)
            .OrderByDescending(kv => kv.Value)
            .Take(3)
            .Select(kv => $"{kv.Key} ({kv.Value:F0}% CPU)")
            .ToList();
    }
}
