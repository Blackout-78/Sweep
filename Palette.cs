using Avalonia.Media;

namespace Sweep;

/// <summary>Night palette (navy + charcoal), same family as the other apps.</summary>
public static class Palette
{
    public static readonly SolidColorBrush Bg = new(Color.Parse("#0B1020"));
    public static readonly SolidColorBrush Surface = new(Color.Parse("#151B2E"));
    public static readonly SolidColorBrush Safe = new(Color.Parse("#3DDC97"));
    public static readonly SolidColorBrush Caution = new(Color.Parse("#FFB020"));
    public static readonly SolidColorBrush Muted = new(Color.Parse("#8A94AD"));
}
