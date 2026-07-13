using Clyr.App.ViewModels;
using Microsoft.UI.Xaml.Controls;
namespace Clyr.App.Pages;

public sealed partial class PrivacyPage : Page
{
    public PrivacyPage(PrivacyViewModel viewModel) { ViewModel = viewModel; InitializeComponent(); PageHost.LayoutModeChanged += (_, mode) => Reflow(mode); }
    public PrivacyViewModel ViewModel { get; }
    public void ResetScroll() => PageHost.ResetScroll();
    private void Reflow(Controls.ResponsivePageWidth mode)
    {
        var narrow = mode == Controls.ResponsivePageWidth.Narrow;
        Grid.SetColumn(DoesNotReadCard, narrow ? 0 : 1);
        Grid.SetRow(DoesNotReadCard, narrow ? 1 : 0);
    }
}
