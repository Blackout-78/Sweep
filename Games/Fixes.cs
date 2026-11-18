using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Microsoft.Win32;

namespace Sweep.Games;

/// <summary>One saved "how to put it back" step. Stored on disk so Undo works after a restart.</summary>
public sealed record UndoRecord(string Kind, string Target, string Name,
    string? OldValue, string? OldKind, bool Existed);

public sealed record FixResult(bool Ok, string Message, List<UndoRecord> Undos);

public interface IFix
{
    string Label { get; }
    FixResult Apply();
}

public sealed record RegEntry(string Path, string Name, object Value, RegistryValueKind Kind);

public sealed class RegistryFix : IFix
{
    readonly RegEntry[] _entries;
    public string Label { get; }

    public RegistryFix(string label, params RegEntry[] entries)
    {
        Label = label;
        _entries = entries;
    }

    public FixResult Apply()
    {
        var undos = new List<UndoRecord>();
        try
        {
            foreach (var e in _entries)
            {
                var (val, kind, exists) = Reg.Read(e.Path, e.Name);
                undos.Add(new UndoRecord("reg", e.Path, e.Name,
                    exists ? Convert.ToString(val, CultureInfo.InvariantCulture) : null,
                    exists ? kind.ToString() : null, exists));
                Reg.Write(e.Path, e.Name, e.Value, e.Kind);
            }
            return new FixResult(true, $"Applied: {Label}", undos);
        }
        catch (Exception ex)
        {
            return new FixResult(false, $"Failed: {Label} ({ex.Message})", undos);
        }
    }
}

public sealed class PowerPlanFix : IFix
{
    public const string HighPerformance = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";
    public const string Ultimate = "e9a42b02-d5df-448d-aa00-03f14749eb61";

    public string Label => "Switch power plan to High performance";

    public FixResult Apply()
    {
        var undos = new List<UndoRecord>();
        var (oldGuid, _) = PowerPlan.GetActive();
        var (code, _) = Shell.Run("powercfg", $"/setactive {HighPerformance}");
        if (code != 0)
            return new FixResult(false, "Failed: the High performance plan is not available on this PC", undos);
        if (oldGuid != null)
            undos.Add(new UndoRecord("power", "powercfg", "active", oldGuid, null, true));
        return new FixResult(true, "Applied: power plan is now High performance", undos);
    }
}

public sealed class PriorityFix : IFix
{
    readonly string _processName;

    public PriorityFix(string processName) => _processName = processName;

    public string Label => "Raise the game's priority to High";

    public FixResult Apply()
    {
        var undos = new List<UndoRecord>();
        try
        {
            int changed = 0;
            foreach (var p in Process.GetProcessesByName(_processName))
            {
                using (p)
                {
                    undos.Add(new UndoRecord("priority", _processName,
                        p.Id.ToString(CultureInfo.InvariantCulture), p.PriorityClass.ToString(), null, true));
                    p.PriorityClass = ProcessPriorityClass.High;
                    changed++;
                }
            }
            return changed > 0
                ? new FixResult(true, "Applied: game priority is now High", undos)
                : new FixResult(false, "Failed: the game is not running", undos);
        }
        catch (Exception ex)
        {
            return new FixResult(false, $"Failed: game priority ({ex.Message})", undos);
        }
    }
}

/// <summary>Undo journal in %AppData%\Sweep\games-undo.json.</summary>
public static class Journal
{
    static string FilePath
    {
        get
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Sweep");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "games-undo.json");
        }
    }

    static List<UndoRecord> Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new List<UndoRecord>();
            return JsonSerializer.Deserialize<List<UndoRecord>>(File.ReadAllText(FilePath))
                   ?? new List<UndoRecord>();
        }
        catch
        {
            return new List<UndoRecord>();
        }
    }

    static void Save(List<UndoRecord> list) =>
        File.WriteAllText(FilePath, JsonSerializer.Serialize(list));

    public static void Add(IEnumerable<UndoRecord> records)
    {
        var list = Load();
        list.AddRange(records);
        Save(list);
    }

    /// <summary>Puts changes back right away (used by Test Run for fixes that did not help).</summary>
    public static bool UndoNow(IEnumerable<UndoRecord> records)
    {
        bool ok = true;
        foreach (var r in records.Reverse()) ok &= UndoOne(r);
        return ok;
    }

    public static string UndoAll()
    {
        var list = Load();
        if (list.Count == 0) return "Nothing to undo.";

        var failed = new List<UndoRecord>();
        int ok = 0;
        for (int i = list.Count - 1; i >= 0; i--)
        {
            if (UndoOne(list[i])) ok++;
            else failed.Add(list[i]);
        }
        failed.Reverse();
        Save(failed);

        return failed.Count == 0
            ? $"Undone: {ok} change(s) put back. Scan again to refresh."
            : $"Undone {ok}, could not undo {failed.Count} (kept in the journal).";
    }

    static bool UndoOne(UndoRecord r)
    {
        try
        {
            if (r.Kind == "reg")
            {
                if (!r.Existed || r.OldValue == null)
                {
                    Reg.Delete(r.Target, r.Name);
                    return true;
                }
                var kind = Enum.Parse<RegistryValueKind>(r.OldKind ?? "String");
                if (kind == RegistryValueKind.DWord)
                    Reg.Write(r.Target, r.Name, int.Parse(r.OldValue, CultureInfo.InvariantCulture), kind);
                else if (kind == RegistryValueKind.String)
                    Reg.Write(r.Target, r.Name, r.OldValue, kind);
                else
                    return false;
                return true;
            }
            if (r.Kind == "priority")
            {
                try
                {
                    using var p = Process.GetProcessById(int.Parse(r.Name, CultureInfo.InvariantCulture));
                    if (p.ProcessName != r.Target) return true; // id reused by another program: nothing to put back
                    p.PriorityClass = Enum.Parse<ProcessPriorityClass>(r.OldValue ?? "Normal");
                    return true;
                }
                catch (ArgumentException)
                {
                    return true; // the game already closed; its priority reset itself
                }
            }
            if (r.Kind == "power")
            {
                var (code, _) = Shell.Run("powercfg", $"/setactive {r.OldValue}");
                return code == 0;
            }
        }
        catch
        {
            // fall through: report as not undone
        }
        return false;
    }
}
