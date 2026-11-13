using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;

namespace DshDesktop;

/// <summary>Shown when nothing serves 127.0.0.1:3080 and no `dsh` exists on this machine.
/// One npm command for every OS, with a copy button. Result: true = retry ("Installed, Retry").</summary>
internal sealed class InstallDshDialog : Window
{
    public const string InstallCommand = "npm install -g @deepseek-ai/dsh";
    private const string NodeUrl = "https://nodejs.org/";

    public InstallDshDialog()
    {
        Title = L10n.InstallTitle;
        Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://DshDesktop/Assets/app.ico")));
        Width = 520;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        CanMinimize = false;
        CanMaximize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var command = new SelectableTextBlock
        {
            Text = InstallCommand,
            FontFamily = new FontFamily("Cascadia Mono, Consolas, Menlo, SF Mono, DejaVu Sans Mono, monospace"),
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
        };

        var copy = new Button { Content = L10n.Copy, MinWidth = 72, HorizontalContentAlignment = HorizontalAlignment.Center };
        copy.Click += async (_, _) =>
        {
            var ok = await CopyAsync(InstallCommand);
            copy.Content = ok ? L10n.Copied : L10n.CopyFailed;
            DispatcherTimer.RunOnce(() => copy.Content = L10n.Copy, TimeSpan.FromSeconds(2));
        };

        var commandRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 12 };
        commandRow.Children.Add(command);
        Grid.SetColumn(copy, 1);
        commandRow.Children.Add(copy);

        var commandBox = new Border
        {
            Background = new SolidColorBrush(Color.Parse("#1A808080")),
            BorderBrush = new SolidColorBrush(Color.Parse("#33808080")),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(14, 10),
            Child = commandRow,
        };

        var node = new Button { Content = L10n.DownloadNode };
        node.Click += (_, _) => MainWindow.OpenExternal(NodeUrl);

        var retry = new Button { Content = L10n.InstalledRetry, Classes = { "accent" } };
        retry.Click += (_, _) => Close(true);

        var close = new Button { Content = L10n.Close };
        close.Click += (_, _) => Close(false);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Thickness(0, 6, 0, 0),
            Children = { node, retry, close },
        };

        Content = new StackPanel
        {
            Margin = new Thickness(24, 20, 24, 20),
            Spacing = 12,
            Children =
            {
                new TextBlock
                {
                    Text = L10n.InstallHeading,
                    FontSize = 16,
                    FontWeight = FontWeight.SemiBold,
                },
                new TextBlock
                {
                    Text = L10n.InstallBody(OperatingSystem.IsWindows()),
                    TextWrapping = TextWrapping.Wrap,
                },
                commandBox,
                new TextBlock
                {
                    Text = L10n.InstallNote,
                    TextWrapping = TextWrapping.Wrap,
                    FontSize = 12,
                    Opacity = 0.7,
                },
                buttons,
            },
        };
    }

    private async Task<bool> CopyAsync(string text)
    {
        try
        {
            if (Clipboard is not { } clipboard) return false;
            await clipboard.SetTextAsync(text);
            return true;
        }
        catch (Exception ex)
        {
            Log.Write("clipboard: " + ex.Message);
            return false;
        }
    }
}
