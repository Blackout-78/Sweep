using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Sweep;

public sealed class PlaceholderView : UserControl
{
    public PlaceholderView(string title, string text)
    {
        Content = new StackPanel
        {
            Margin = new Thickness(24),
            Spacing = 8,
            Children =
            {
                new TextBlock { Text = title, FontSize = 22, FontWeight = FontWeight.Bold },
                new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Opacity = 0.8 }
            }
        };
    }
}
