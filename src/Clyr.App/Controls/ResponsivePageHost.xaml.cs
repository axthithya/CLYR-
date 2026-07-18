using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Clyr.App.Controls;

public enum ResponsivePageWidth
{
    Narrow,
    Medium,
    Wide
}

public sealed partial class ResponsivePageHost : UserControl
{
    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(string), typeof(ResponsivePageHost), new PropertyMetadata(string.Empty));
    public static readonly DependencyProperty SubtitleProperty = DependencyProperty.Register(
        nameof(Subtitle), typeof(string), typeof(ResponsivePageHost), new PropertyMetadata(string.Empty));
    public static readonly DependencyProperty PageContentProperty = DependencyProperty.Register(
        nameof(PageContent), typeof(object), typeof(ResponsivePageHost), new PropertyMetadata(null));

    public ResponsivePageHost() => InitializeComponent();

    public event EventHandler<ResponsivePageWidth>? LayoutModeChanged;

    public string Title { get => (string)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string Subtitle { get => (string)GetValue(SubtitleProperty); set => SetValue(SubtitleProperty, value); }
    public object PageContent { get => GetValue(PageContentProperty); set => SetValue(PageContentProperty, value); }
    public ResponsivePageWidth LayoutMode { get; private set; } = ResponsivePageWidth.Wide;

    public void ResetScroll() => PageScroll.ChangeView(null, 0, null, true);

    private void ViewportSizeChanged(object sender, SizeChangedEventArgs args)
    {
        var width = Math.Max(0, args.NewSize.Width);
        var desktopWidth = (double)Application.Current.Resources["ContentMaxWidthDesktop"];
        ViewportSurface.Width = width;
        ContentContainer.Width = Math.Min(desktopWidth, width);
        ContentContainer.Padding = width switch
        {
            < 760 => (Thickness)Application.Current.Resources["PageMarginNarrow"],
            < 1200 => (Thickness)Application.Current.Resources["PageMarginMedium"],
            _ => (Thickness)Application.Current.Resources["PageMarginDesktop"]
        };

        var next = width switch
        {
            < 760 => ResponsivePageWidth.Narrow,
            < 1200 => ResponsivePageWidth.Medium,
            _ => ResponsivePageWidth.Wide
        };
        LayoutMode = next;
        LayoutModeChanged?.Invoke(this, next);
    }
}
