using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Sweep.Games;

/// <summary>Tiny HKCU registry helper.</summary>
public static class Reg
{
    public static (object? val, RegistryValueKind kind, bool exists) Read(string path, string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(path);
        if (key == null) return (null, RegistryValueKind.Unknown, false);
        var v = key.GetValue(name);
        if (v == null) return (null, RegistryValueKind.Unknown, false);
        return (v, key.GetValueKind(name), true);
    }

    public static void Write(string path, string name, object value, RegistryValueKind kind)
    {
        using var key = Registry.CurrentUser.CreateSubKey(path, true);
        key.SetValue(name, value, kind);
    }

    public static void Delete(string path, string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(path, true);
        key?.DeleteValue(name, false);
    }
}

public static class Shell
{
    public static (int code, string output) Run(string file, string args)
    {
        var psi = new ProcessStartInfo(file, args)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var p = Process.Start(psi)!;
        var text = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
        p.WaitForExit(15000);
        return (p.ExitCode, text);
    }
}

public static class PowerPlan
{
    public static (string? guid, string? name) GetActive()
    {
        var (code, output) = Shell.Run("powercfg", "/getactivescheme");
        if (code != 0) return (null, null);
        var m = Regex.Match(output,
            @"([0-9a-fA-F]{8}(-[0-9a-fA-F]{4}){3}-[0-9a-fA-F]{12})\s*\(([^)]*)\)");
        return m.Success ? (m.Groups[1].Value.ToLowerInvariant(), m.Groups[3].Value) : (null, null);
    }
}
