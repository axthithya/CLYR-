using Clyr.App.Controls;
using Clyr.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace Clyr.App.Pages;

public sealed partial class DeveloperModePage : Page
{
    private static readonly string[] Tools =
    [
        "Docker", "WSL", "Node.js", "Android", "Flutter", "Gradle / Maven",
        "NuGet", "Python", "Rust", "Playwright", "Build output"
    ];

    public DeveloperModePage(DeveloperModeViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        PageHost.LayoutModeChanged += (_, mode) => Reflow(mode);
        Reflow(ResponsivePageWidth.Wide);
    }

    public DeveloperModeViewModel ViewModel { get; }
    public void ResetScroll() => PageHost.ResetScroll();

    private void Reflow(ResponsivePageWidth mode)
    {
        var columns = mode switch { ResponsivePageWidth.Wide => 3, ResponsivePageWidth.Medium => 2, _ => 1 };
        ToolGrid.Children.Clear();
        ToolGrid.ColumnDefinitions.Clear();
        ToolGrid.RowDefinitions.Clear();
        for (var column = 0; column < columns; column++) ToolGrid.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        for (var row = 0; row < (int)Math.Ceiling(Tools.Length / (double)columns); row++) ToolGrid.RowDefinitions.Add(new() { Height = GridLength.Auto });

        for (var index = 0; index < Tools.Length; index++)
        {
            var title = new TextBlock { Text = Tools[index], Style = (Style)Application.Current.Resources["CardTitleStyle"] };
            var status = new TextBlock { Text = "Preview only · unavailable", Style = (Style)Application.Current.Resources["BodyMutedStyle"] };
            var content = new StackPanel { Spacing = 6 };
            content.Children.Add(title);
            content.Children.Add(status);
            var tile = new Border { Style = (Style)Application.Current.Resources["CardStyle"], Child = content };
            AutomationProperties.SetName(tile, Tools[index] + " preview tile");
            Grid.SetColumn(tile, index % columns);
            Grid.SetRow(tile, index / columns);
            ToolGrid.Children.Add(tile);
        }
    }
}
