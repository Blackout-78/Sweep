using System.Diagnostics;
using Microsoft.Win32;

namespace Sweep.Games;

/// <summary>Reads Windows settings and reports what can cause lag, jitter or screen tearing.
/// Read-only: nothing is changed here. Fixes are separate objects the user approves.</summary>
public static class Scanner
{
    const string GameBar = @"Software\Microsoft\GameBar";
    const string GameConfig = @"System\GameConfigStore";
    const string GameDvr = @"Software\Microsoft\Windows\CurrentVersion\GameDVR";
    const string MouseKey = @"Control Panel\Mouse";
    const string GpuPrefs = @"Software\Microsoft\DirectX\UserGpuPreferences";
    const string Layers = @"Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers";
    const string FsoFlag = "DISABLEDXMAXIMIZEDWINDOWEDMODE";

    static readonly HashSet<string> IgnoreProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Sweep", "System", "Registry", "Memory Compression", "dwm", "Idle"
    };

    public static List<Finding> Scan(string? exe)
    {
        var list = new List<Finding>();
        Add(list, "Power plan", CheckPower);
        Add(list, "Game Mode", CheckGameMode);
        Add(list, "Background recording", CheckGameDvr);
        Add(list, "Mouse acceleration", CheckMouse);
        if (exe != null)
        {
            Add(list, "GPU preference", () => CheckGpuPreference(exe));
            Add(list, "Fullscreen optimizations", () => CheckFso(exe));
            Add(list, "Disk space", () => CheckDisk(exe));
        }
        Add(list, "GPU scheduling", CheckHags);
        Add(list, "Memory", CheckRam);
        Add(list, "Background apps", () => CheckBackground(exe));
        return list;
    }

    static void Add(List<Finding> list, string name, Func<Finding?> check)
    {
        try
        {
            var f = check();
            if (f != null) list.Add(f);
        }
        catch (Exception ex)
        {
            list.Add(Info($"{name}: check failed", ex.Message, Category.General));
        }
    }

    static Finding Info(string title, string detail, Category cat) => new()
    {
        Title = title,
        Detail = detail,
        Risk = Risk.ReportOnly,
        Category = cat,
        IsProblem = false
    };

    static Finding CheckPower()
    {
        var (guid, name) = PowerPlan.GetActive();
        if (guid == null)
            return Info("Power plan", "Could not read the active power plan.", Category.Lag);

        bool good = guid == PowerPlanFix.HighPerformance || guid == PowerPlanFix.Ultimate;
        return new Finding
        {
            Title = good ? $"Power plan: {name}" : $"Power plan is \"{name}\"",
            Detail = good
                ? "Already a performance plan."
                : "Balanced and Power saver can lower CPU speed in the middle of a game, " +
                  "which shows up as lag and jitter. High performance uses more power " +
                  "(on a laptop the battery drains faster). Some newer laptops only allow " +
                  "Balanced; if so, this fix will report that it failed and change nothing.",
            Risk = Risk.Safe,
            Category = Category.Lag,
            IsProblem = !good,
            Fix = good ? null : new PowerPlanFix()
        };
    }

    static Finding CheckGameMode()
    {
        var r = Reg.Read(GameBar, "AutoGameModeEnabled");
        bool off = r.exists && Convert.ToInt32(r.val) == 0;
        return new Finding
        {
            Title = off ? "Windows Game Mode is off" : "Windows Game Mode is on",
            Detail = off
                ? "Game Mode tells Windows to favor the game over background work."
                : "Nothing to fix.",
            Risk = Risk.Safe,
            Category = Category.Jitter,
            IsProblem = off,
            Fix = off
                ? new RegistryFix("Turn on Windows Game Mode",
                    new RegEntry(GameBar, "AutoGameModeEnabled", 1, RegistryValueKind.DWord))
                : null
        };
    }

    static Finding CheckGameDvr()
    {
        var a = Reg.Read(GameConfig, "GameDVR_Enabled");
        var b = Reg.Read(GameDvr, "AppCaptureEnabled");
        bool on = (a.exists && Convert.ToInt32(a.val) == 1) || (b.exists && Convert.ToInt32(b.val) == 1);
        return new Finding
        {
            Title = on ? "Xbox Game Bar background recording is on" : "Background recording is off",
            Detail = on
                ? "Background capture uses GPU and disk while you play and can cause frame drops."
                : "Nothing to fix.",
            Risk = Risk.Safe,
            Category = Category.Jitter,
            IsProblem = on,
            Fix = on
                ? new RegistryFix("Turn off Game Bar background recording",
                    new RegEntry(GameConfig, "GameDVR_Enabled", 0, RegistryValueKind.DWord),
                    new RegEntry(GameDvr, "AppCaptureEnabled", 0, RegistryValueKind.DWord))
                : null
        };
    }

    static Finding CheckMouse()
    {
        var r = Reg.Read(MouseKey, "MouseSpeed");
        bool accel = r.exists && Convert.ToString(r.val) != "0";
        return new Finding
        {
            Title = accel ? "Mouse pointer acceleration is on" : "Mouse acceleration is off",
            Detail = accel
                ? "Acceleration can make aiming inconsistent in games that use the Windows cursor. " +
                  "Many shooters read the mouse directly and ignore this setting, so the gain " +
                  "varies. It changes the cursor feel on the whole PC and applies after you " +
                  "sign out and back in."
                : "Nothing to fix.",
            Risk = Risk.Safe,
            Category = Category.General,
            IsProblem = accel,
            Fix = accel
                ? new RegistryFix("Turn off mouse acceleration",
                    new RegEntry(MouseKey, "MouseSpeed", "0", RegistryValueKind.String),
                    new RegEntry(MouseKey, "MouseThreshold1", "0", RegistryValueKind.String),
                    new RegEntry(MouseKey, "MouseThreshold2", "0", RegistryValueKind.String))
                : null
        };
    }

    static Finding CheckGpuPreference(string exe)
    {
        var r = Reg.Read(GpuPrefs, exe);
        bool set = r.exists && (Convert.ToString(r.val) ?? "").Contains("GpuPreference=2");
        return new Finding
        {
            Title = set ? "Game is set to use the high-performance GPU" : "Game GPU preference is not set to High performance",
            Detail = set
                ? "Nothing to fix."
                : "On PCs with two GPUs (most laptops) Windows may run the game on the weak one. " +
                  "Harmless on a single-GPU PC.",
            Risk = Risk.Safe,
            Category = Category.Lag,
            IsProblem = !set,
            Fix = set
                ? null
                : new RegistryFix("Set this game to use the high-performance GPU",
                    new RegEntry(GpuPrefs, exe, "GpuPreference=2;", RegistryValueKind.String))
        };
    }

    static Finding CheckFso(string exe)
    {
        var r = Reg.Read(Layers, exe);
        var existing = r.exists ? Convert.ToString(r.val) ?? "" : "";
        bool has = existing.Contains(FsoFlag);
        var newValue = has
            ? existing
            : existing.Length == 0 ? "~ " + FsoFlag : existing + " " + FsoFlag;
        return new Finding
        {
            Title = has
                ? "Fullscreen optimizations are already off for this game"
                : "Option: turn off fullscreen optimizations for this game",
            Detail = has
                ? "Nothing to fix."
                : "Try this if you see tearing or stutter in fullscreen. For some games it helps, " +
                  "for others it makes no difference, so test after applying. Undo puts it back.",
            Risk = Risk.Caution,
            Category = Category.Tearing,
            IsProblem = !has,
            Fix = has
                ? null
                : new RegistryFix("Turn off fullscreen optimizations for this game",
                    new RegEntry(Layers, exe, newValue, RegistryValueKind.String))
        };
    }

    static Finding CheckDisk(string exe)
    {
        var root = Path.GetPathRoot(exe);
        if (string.IsNullOrEmpty(root))
            return Info("Disk space", "Could not read the game's drive.", Category.Lag);

        var drive = new DriveInfo(root);
        double pct = 100.0 * drive.AvailableFreeSpace / drive.TotalSize;
        double gb = drive.AvailableFreeSpace / 1024.0 / 1024 / 1024;
        bool low = pct < 10;
        return new Finding
        {
            Title = low ? $"The game's drive is almost full ({gb:F0} GB free)" : $"Game drive has {gb:F0} GB free",
            Detail = low
                ? "A nearly full drive slows loading and can cause hitching. Free some space " +
                  "(Sweep's Cleaner tab will help here)."
                : "Nothing to fix.",
            Risk = Risk.ReportOnly,
            Category = Category.Lag,
            IsProblem = low
        };
    }

    static Finding CheckHags()
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\GraphicsDrivers");
        var v = key?.GetValue("HwSchMode");
        string state = v == null ? "not available (older GPU or driver)"
            : Convert.ToInt32(v) == 2 ? "ON" : "OFF";
        return Info($"Hardware GPU scheduling: {state}",
            "Info only. If a game stutters, try flipping it in Windows Settings > System > Display > " +
            "Graphics > Default graphics settings, then retest. Sweep reports it but does not change it yet.",
            Category.Jitter);
    }

    static Finding CheckRam()
    {
        double gb = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / 1024.0 / 1024 / 1024;
        bool low = gb < 8;
        return new Finding
        {
            Title = low ? $"Low memory: {gb:F1} GB RAM" : $"Memory: {gb:F1} GB RAM",
            Detail = low
                ? "Under 8 GB, Windows swaps to disk during games, which causes stutter. " +
                  "Only more RAM fixes this."
                : "Enough for most games.",
            Risk = Risk.ReportOnly,
            Category = Category.Lag,
            IsProblem = low
        };
    }

    static Finding CheckBackground(string? exe)
    {
        var gameName = exe != null ? Path.GetFileNameWithoutExtension(exe) : null;
        var totals = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                if (p.ProcessName == gameName || IgnoreProcesses.Contains(p.ProcessName)) continue;
                long mb = p.WorkingSet64 / 1048576;
                totals[p.ProcessName] = totals.GetValueOrDefault(p.ProcessName) + mb;
            }
            catch
            {
                // protected process: skip
            }
            finally
            {
                p.Dispose();
            }
        }

        var heavy = totals.Where(kv => kv.Value >= 500).OrderByDescending(kv => kv.Value).Take(5).ToList();
        if (heavy.Count == 0)
            return Info("Background apps", "No big memory users running.", Category.Lag);

        var names = string.Join(", ", heavy.Select(kv => $"{kv.Key} ({kv.Value / 1024.0:F1} GB)"));
        return new Finding
        {
            Title = "Heavy apps are running in the background",
            Detail = $"{names}. Close them before gaming. A later Sweep release will pause approved ones for you.",
            Risk = Risk.ReportOnly,
            Category = Category.Lag,
            IsProblem = true
        };
    }
}
