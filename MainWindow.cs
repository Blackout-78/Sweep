using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace Sweep;

public sealed class MainWindow : Window
{
    public MainWindow()
    {
        Title = AppInfo.Name;
        Width = 1000;
        Height = 700;
        MinWidth = 760;
        MinHeight = 520;
        Background = Palette.Bg;

        var tabs = new TabControl();
        tabs.Items.Add(new TabItem
        {
            Header = "Privacy",
            Content = new PlaceholderView("Privacy",
                "Coming next: privacy.sexy-style cleanup (telemetry, history, traces).")
        });
        tabs.Items.Add(new TabItem
        {
            Header = "Cleaner",
            Content = new PlaceholderView("Cleaner",
                "Coming next: BleachBit-style junk cleaner (temp files, caches, logs).")
        });
        tabs.Items.Add(new TabItem
        {
            Header = "Tweaks",
            Content = new PlaceholderView("Tweaks",
                "Coming next: Chris Titus-style Windows tweaks with Safe/Caution tags.")
        });
        tabs.Items.Add(new TabItem { Header = "Games", Content = new GamesView() });
        tabs.Items.Add(new TabItem { Header = "Settings", Content = new SettingsView() });
        tabs.SelectedIndex = 3; // open on Games

        var stamp = new TextBlock
        {
            Text = AppInfo.Stamp,
            FontSize = 11,
            Opacity = 0.6,
            Margin = new Thickness(10, 4),
            HorizontalAlignment = HorizontalAlignment.Left
        };

        var root = new DockPanel();
        DockPanel.SetDock(stamp, Dock.Bottom);
        root.Children.Add(stamp);
        root.Children.Add(tabs);
        Content = root;
    }
}
