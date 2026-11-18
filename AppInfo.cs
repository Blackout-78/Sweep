using System.Reflection;

namespace Sweep;

public static class AppInfo
{
    public const string Name = "Sweep";

    /// <summary>Real release name. Change it together with RELEASE_NOTES.md for each release.</summary>
    public const string ReleaseName = "Games Scan";

    public const string ReleasesUrl = "https://github.com/Blackout-78/Sweep/releases";

    static readonly System.Version V =
        Assembly.GetExecutingAssembly().GetName().Version ?? new System.Version(0, 0, 0, 0);

    public static string VersionText => $"{V.Major}.{V.Minor}.{Math.Max(V.Build, 0)}";
    public static int Build => Math.Max(V.Revision, 0);

    /// <summary>Small tag shown at the bottom-left of the window.</summary>
    public static string Stamp => $"v{VersionText} · build {Build}";

    /// <summary>Full version shown in Settings.</summary>
    public static string Full => $"{Name} v{VersionText} \"{ReleaseName}\" (build {Build})";
}
