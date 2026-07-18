using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Clyr.App.Controls;

public sealed partial class SectionHeader : UserControl
{
    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(string), typeof(SectionHeader), new PropertyMetadata(string.Empty));
    public static readonly DependencyProperty DescriptionProperty = DependencyProperty.Register(
        nameof(Description), typeof(string), typeof(SectionHeader), new PropertyMetadata(string.Empty));
    public static readonly DependencyProperty ActionContentProperty = DependencyProperty.Register(
        nameof(ActionContent), typeof(object), typeof(SectionHeader), new PropertyMetadata(null));

    public SectionHeader() => InitializeComponent();

    public string Title { get => (string)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string Description { get => (string)GetValue(DescriptionProperty); set => SetValue(DescriptionProperty, value); }
    public object ActionContent { get => GetValue(ActionContentProperty); set => SetValue(ActionContentProperty, value); }
}
