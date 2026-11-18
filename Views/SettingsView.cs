using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Sweep;

public sealed class SettingsView : UserControl
{
    public SettingsView()
    {
        var open = new Button { Content = "Open GitHub Releases" };
        open.Click += (_, _) =>
        {
            try
            {
                Process.Start(new ProcessStartInfo(AppInfo.ReleasesUrl) { UseShellExecute = true });
            }
            catch
            {
                // no browser available: nothing to do, app stays offline
            }
        };

        Content = new StackPanel
        {
            Margin = new Thickness(24),
            Spacing = 10,
            Children =
            {
                new TextBlock { Text = "Settings", FontSize = 22, FontWeight = FontWeight.Bold },
                new TextBlock { Text = "Version", FontWeight = FontWeight.SemiBold },
                new TextBlock { Text = AppInfo.Full },
                new TextBlock { Text = "Updates", FontWeight = FontWeight.SemiBold },
                new TextBlock
                {
                    Text = "Sweep is fully offline. This button only opens your browser on the " +
                           "GitHub releases page; the app itself never connects to the internet.",
                    TextWrapping = TextWrapping.Wrap,
                    Opacity = 0.8
                },
                open
            }
        };
    }
}
