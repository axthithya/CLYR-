using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Clyr.App.Controls;

public sealed partial class KeyValueRow : UserControl
{
    public static readonly DependencyProperty LabelProperty = DependencyProperty.Register(
        nameof(Label), typeof(string), typeof(KeyValueRow), new PropertyMetadata(string.Empty));
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(string), typeof(KeyValueRow), new PropertyMetadata(string.Empty));

    public KeyValueRow() => InitializeComponent();

    public string Label { get => (string)GetValue(LabelProperty); set => SetValue(LabelProperty, value); }
    public string Value { get => (string)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
}
