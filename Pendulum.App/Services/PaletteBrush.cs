using System.Windows;
using System.Windows.Controls;
using System.Windows.Shapes;

namespace Pendulum.App.Services;

/// Paints an element in one of the palette's colours, by its name ("teal", as a reminder saves
/// it): the theme's brush for it, by resource reference, so switching the theme repaints it. In
/// XAML: `services:PaletteBrush.Background="{Binding DisplayColor}"` (the tint) on a Border,
/// `PaletteBrush.BorderBrush` (the shade) on a Border, `PaletteBrush.Fill` (the shade) on a
/// Shape. No name (null) clears it, so a style's own value applies again.
public static class PaletteBrush
{
    public static readonly DependencyProperty BackgroundProperty = DependencyProperty.RegisterAttached(
        "Background", typeof(string), typeof(PaletteBrush), new PropertyMetadata(null, (d, e) => // not user-visible: property names
            Apply(d, e.NewValue as string, tint: true, d is Border ? Border.BackgroundProperty : Control.BackgroundProperty)));

    public static readonly DependencyProperty BorderBrushProperty = DependencyProperty.RegisterAttached(
        "BorderBrush", typeof(string), typeof(PaletteBrush), new PropertyMetadata(null, (d, e) => // not user-visible
            Apply(d, e.NewValue as string, tint: false, d is Border ? Border.BorderBrushProperty : Control.BorderBrushProperty)));

    public static readonly DependencyProperty FillProperty = DependencyProperty.RegisterAttached(
        "Fill", typeof(string), typeof(PaletteBrush), new PropertyMetadata(null, (d, e) => // not user-visible
            Apply(d, e.NewValue as string, tint: false, Shape.FillProperty)));

    public static string? GetBackground(DependencyObject d) => (string?)d.GetValue(BackgroundProperty);
    public static void SetBackground(DependencyObject d, string? value) => d.SetValue(BackgroundProperty, value);
    public static string? GetBorderBrush(DependencyObject d) => (string?)d.GetValue(BorderBrushProperty);
    public static void SetBorderBrush(DependencyObject d, string? value) => d.SetValue(BorderBrushProperty, value);
    public static string? GetFill(DependencyObject d) => (string?)d.GetValue(FillProperty);
    public static void SetFill(DependencyObject d, string? value) => d.SetValue(FillProperty, value);

    /// The theme's resource key for a palette colour's shade or tint, by its saved name; null for
    /// a name that isn't one of the palette's.
    public static string? KeyFor(string? name, bool tint)
    {
        var color = AppThemeManager.Palette.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
        return color is null ? null : tint ? AppThemeManager.PaletteTintBrushKey(color.Name) : AppThemeManager.PaletteBrushKey(color.Name);
    }

    private static void Apply(DependencyObject d, string? name, bool tint, DependencyProperty target)
    {
        if (d is not FrameworkElement element)
            return;
        if (KeyFor(name, tint) is { } key)
            element.SetResourceReference(target, key);
        else
            element.ClearValue(target);
    }
}
