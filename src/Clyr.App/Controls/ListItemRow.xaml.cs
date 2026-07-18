using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Clyr.App.Controls;

public sealed partial class ListItemRow : UserControl
{
    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(string), typeof(ListItemRow), new PropertyMetadata(string.Empty));
    public static readonly DependencyProperty DetailProperty = DependencyProperty.Register(
        nameof(Detail), typeof(string), typeof(ListItemRow), new PropertyMetadata(string.Empty));
    public static readonly DependencyProperty TrailingTextProperty = DependencyProperty.Register(
        nameof(TrailingText), typeof(string), typeof(ListItemRow), new PropertyMetadata(string.Empty));
    public static readonly DependencyProperty GlyphProperty = DependencyProperty.Register(
        nameof(Glyph), typeof(string), typeof(ListItemRow), new PropertyMetadata("\uE8B7"));

    public ListItemRow() => InitializeComponent();

    public string Title { get => (string)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string Detail { get => (string)GetValue(DetailProperty); set => SetValue(DetailProperty, value); }
    public string TrailingText { get => (string)GetValue(TrailingTextProperty); set => SetValue(TrailingTextProperty, value); }
    public string Glyph { get => (string)GetValue(GlyphProperty); set => SetValue(GlyphProperty, value); }
}
