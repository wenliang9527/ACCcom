using System.Windows;
using System.Windows.Controls;

namespace ACCcom.Controls;

/// <summary>
/// Card container that overlays a subtle canvas-grain texture over its
/// background, giving surfaces a painted, non-flat feel. Usage is identical
/// to Border (Background / CornerRadius / Effect / Padding), but content is
/// set as Content instead of Child.
/// </summary>
public class PaintedCard : ContentControl
{
    public static readonly DependencyProperty CornerRadiusProperty = DependencyProperty.Register(
        nameof(CornerRadius), typeof(CornerRadius), typeof(PaintedCard),
        new PropertyMetadata(new CornerRadius(8)));

    public CornerRadius CornerRadius
    {
        get => (CornerRadius)GetValue(CornerRadiusProperty);
        set => SetValue(CornerRadiusProperty, value);
    }

    static PaintedCard()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(PaintedCard), new FrameworkPropertyMetadata(typeof(PaintedCard)));
    }
}
