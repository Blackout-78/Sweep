namespace Sweep.Games;

/// <summary>Safe = reversible, no real downside. Caution = reversible, may have a trade-off.
/// ReportOnly = Sweep only tells you; no fix.</summary>
public enum Risk { Safe, Caution, ReportOnly }

public enum Category { Lag, Jitter, Tearing, General }

public sealed class Finding
{
    public required string Title { get; init; }
    public required string Detail { get; init; }
    public required Risk Risk { get; init; }
    public required Category Category { get; init; }

    /// <summary>false = this check looks fine (shown as OK / info).</summary>
    public bool IsProblem { get; init; } = true;

    /// <summary>true = Test Run can apply this fix live and measure the effect (no game restart).</summary>
    public bool LiveTestable { get; init; }

    public IFix? Fix { get; init; }
    public bool Fixed { get; set; }
}
