using Clyr.App.ViewModels;
using Microsoft.UI.Xaml.Controls;
namespace Clyr.App.Pages;

public sealed partial class AboutPage : Page
{
    public AboutPage(AboutViewModel viewModel) { ViewModel = viewModel; InitializeComponent(); VersionText.Text = "Version " + viewModel.Version; TechnicalText.Text = viewModel.TechnicalDetails; PageHost.LayoutModeChanged += (_, mode) => Reflow(mode); }
    public AboutViewModel ViewModel { get; }
    public void ResetScroll() => PageHost.ResetScroll();
    private void Reflow(Controls.ResponsivePageWidth mode)
    {
        var narrow = mode == Controls.ResponsivePageWidth.Narrow;
        Grid.SetColumn(ProductCopy, narrow ? 0 : 1);
        Grid.SetRow(ProductCopy, narrow ? 1 : 0);
        Grid.SetColumn(UnavailableCard, narrow ? 0 : 1);
        Grid.SetRow(UnavailableCard, narrow ? 1 : 0);
    }
}
