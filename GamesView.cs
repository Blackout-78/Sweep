using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Sweep.Games;

namespace Sweep;

public sealed class GamesView : UserControl
{
    readonly TextBlock _exeText = new()
    {
        Text = "No game selected (system-wide scan only)",
        TextWrapping = TextWrapping.Wrap,
        Opacity = 0.8,
        VerticalAlignment = VerticalAlignment.Center
    };

    readonly ComboBox _mode = new();
    readonly StackPanel _list = new() { Spacing = 8 };
    readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, FontWeight = FontWeight.SemiBold };

    readonly Button _testRun = new() { Content = "Test Run" };
    readonly Button _stop = new() { Content = "Stop", IsEnabled = false };
    readonly StackPanel _log = new() { Spacing = 4 };
    CancellationTokenSource? _cts;

    List<Finding> _findings = new();
    string? _exe;

    public GamesView()
    {
        var pick = new Button { Content = "Choose game .exe…" };
        pick.Click += async (_, _) => await PickGame();

        _mode.ItemsSource = new[]
        {
            "Report only",
            "One-click fixes (with undo)",
            "Auto-fix Safe items"
        };
        _mode.SelectedIndex = 1;
        _mode.SelectionChanged += (_, _) => Render();

        var scan = new Button { Content = "Scan" };
        scan.Click += async (_, _) => await DoScan();

        var undo = new Button { Content = "Undo all changes" };
        undo.Click += async (_, _) =>
        {
            _status.Text = await Task.Run(Journal.UndoAll);
        };

        _testRun.Click += async (_, _) => await DoTestRun();
        _stop.Click += (_, _) => _cts?.Cancel();

        var row1 = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            Children = { pick, _exeText }
        };
        var row2 = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            Children = { _mode, scan, undo, _testRun, _stop }
        };

        var root = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 12,
            Children =
            {
                new TextBlock { Text = "Games", FontSize = 22, FontWeight = FontWeight.Bold },
                new TextBlock
                {
                    Text = "Finds what causes lag, jitter and screen tearing. Offline. It never " +
                           "touches game files or injects into games, and every fix can be undone.",
                    TextWrapping = TextWrapping.Wrap,
                    Opacity = 0.8
                },
                row1,
                row2,
                _status,
                _log,
                _list
            }
        };

        Content = new ScrollViewer { Content = root };
    }

    async Task PickGame()
    {
        var top = TopLevel.GetTopLevel(this);
        if (top == null) return;

        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choose the game's .exe",
            AllowMultiple = false,
            FileTypeFilter = new[] { new FilePickerFileType("Programs") { Patterns = new[] { "*.exe" } } }
        });
        if (files.Count == 0) return;

        _exe = files[0].TryGetLocalPath();
        _exeText.Text = _exe ?? "No game selected (system-wide scan only)";
    }

    async Task DoTestRun()
    {
        if (_exe == null)
        {
            _status.Text = "Choose the game .exe first, start the game, then click Test Run.";
            return;
        }

        _log.Children.Clear();
        _cts = new CancellationTokenSource();
        _testRun.IsEnabled = false;
        _stop.IsEnabled = true;
        var progress = new Progress<string>(line =>
        {
            _status.Text = line;
            _log.Children.Add(new TextBlock { Text = line, TextWrapping = TextWrapping.Wrap, Opacity = 0.85 });
        });
        try
        {
            await TestRun.RunAsync(_exe, progress, _cts.Token);
        }
        catch (OperationCanceledException)
        {
            _status.Text = "Test Run stopped. Any change that was not yet measured was undone.";
        }
        catch (Exception ex)
        {
            _status.Text = "Test Run failed: " + ex.Message;
        }
        finally
        {
            _testRun.IsEnabled = true;
            _stop.IsEnabled = false;
            _cts?.Dispose();
            _cts = null;
        }
    }

    async Task DoScan()
    {
        _status.Text = "Scanning…";
        _findings = await Task.Run(() => Scanner.Scan(_exe));

        int autoFixed = 0;
        if (_mode.SelectedIndex == 2)
        {
            foreach (var f in _findings.Where(x => x.IsProblem && x.Fix != null && x.Risk == Risk.Safe))
            {
                var r = await Task.Run(() => f.Fix!.Apply());
                if (r.Undos.Count > 0) Journal.Add(r.Undos);
                f.Fixed = r.Ok;
                if (r.Ok) autoFixed++;
            }
        }

        int problems = _findings.Count(x => x.IsProblem && !x.Fixed);
        _status.Text = _mode.SelectedIndex == 2
            ? $"Done. {autoFixed} Safe item(s) fixed, {problems} still need a look."
            : $"Done. {problems} issue(s) found.";
        Render();
    }

    async Task ApplyFix(Finding f)
    {
        var r = await Task.Run(() => f.Fix!.Apply());
        if (r.Undos.Count > 0) Journal.Add(r.Undos);
        f.Fixed = r.Ok;
        _status.Text = r.Message;
        Render();
    }

    void Render()
    {
        _list.Children.Clear();
        int mode = _mode.SelectedIndex;
        foreach (var f in _findings.OrderByDescending(x => x.IsProblem && !x.Fixed).ThenBy(x => x.Risk))
            _list.Children.Add(Card(f, mode));
    }

    Control Card(Finding f, int mode)
    {
        (string Text, SolidColorBrush Color) tag =
            f.Fixed ? ("FIXED", Palette.Safe)
            : !f.IsProblem ? ("OK", Palette.Muted)
            : f.Risk == Risk.Safe ? ("SAFE FIX", Palette.Safe)
            : f.Risk == Risk.Caution ? ("CAUTION", Palette.Caution)
            : ("REPORT", Palette.Muted);

        var tagBorder = new Border
        {
            Background = tag.Color,
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(6, 2),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = tag.Text,
                FontSize = 11,
                FontWeight = FontWeight.Bold,
                Foreground = Palette.Bg
            }
        };

        var body = new StackPanel { Spacing = 6 };
        body.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children =
            {
                tagBorder,
                new TextBlock
                {
                    Text = f.Fixed && f.Fix != null ? "Fixed: " + f.Fix.Label : f.Title,
                    FontWeight = FontWeight.SemiBold,
                    VerticalAlignment = VerticalAlignment.Center
                },
                new TextBlock
                {
                    Text = f.Category.ToString(),
                    FontSize = 11,
                    Opacity = 0.6,
                    VerticalAlignment = VerticalAlignment.Center
                }
            }
        });
        body.Children.Add(new TextBlock
        {
            Text = f.Fixed ? "Applied. Use \"Undo all changes\" to put it back." : f.Detail,
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.8
        });

        if (f.IsProblem && !f.Fixed && f.Fix != null)
        {
            if (mode == 0)
            {
                body.Children.Add(new TextBlock
                {
                    Text = "Fix available in One-click mode.",
                    FontSize = 12,
                    Opacity = 0.6
                });
            }
            else
            {
                var btn = new Button { Content = "Fix: " + f.Fix.Label };
                btn.Click += async (_, _) => await ApplyFix(f);
                body.Children.Add(btn);
            }
        }

        return new Border
        {
            Background = Palette.Surface,
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12),
            Child = body
        };
    }
}
