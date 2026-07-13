using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Clyr.App;

public sealed class StartupErrorWindow : Window
{
    public StartupErrorWindow(Exception exception)
    {
        Title = "CLYR startup error";
        Content = new StackPanel
        {
            Padding = new Thickness(32),
            Spacing = 12,
            Children =
            {
                new TextBlock { Text = "CLYR could not start.", FontSize = 28 },
                new TextBlock { Text = "No drives were scanned and no files were changed." },
                new TextBlock { Text = exception.GetType().Name + ": " + exception.Message, TextWrapping = TextWrapping.Wrap }
            }
        };
    }
}
