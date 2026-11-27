using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace Mountly.App.Controls;

/// <summary>
/// Draws a drive: an iridescent disc while an image is loaded, a dashed outline while the drive is empty.
/// The sheen uses the same colours as the app icon. Add the "busy" class to spin it (see MainWindow styles).
/// </summary>
public sealed class DiscGlyph : Control
{
    public static readonly StyledProperty<bool> HasDiscProperty = AvaloniaProperty.Register<DiscGlyph, bool>(nameof(HasDisc));

    /// <summary>Fills the centre hole, normally the card's background.</summary>
    public static readonly StyledProperty<IBrush?> HoleBrushProperty = AvaloniaProperty.Register<DiscGlyph, IBrush?>(nameof(HoleBrush));

    /// <summary>Outline colour of an empty drive.</summary>
    public static readonly StyledProperty<IBrush?> OutlineBrushProperty = AvaloniaProperty.Register<DiscGlyph, IBrush?>(nameof(OutlineBrush));

    private static readonly IBrush Sheen = new ConicGradientBrush
    {
        Angle = 200,
        GradientStops =
        {
            new GradientStop(Color.Parse("#2DD4BF"), 0.00),
            new GradientStop(Color.Parse("#60A5FA"), 0.22),
            new GradientStop(Color.Parse("#8B5CF6"), 0.42),
            new GradientStop(Color.Parse("#FF4FA3"), 0.62),
            new GradientStop(Color.Parse("#F5B84A"), 0.80),
            new GradientStop(Color.Parse("#2DD4BF"), 1.00),
        },
    }.ToImmutable();

    // A soft shine across the disc, and grooves: faint concentric rings over the sheen.
    private static readonly IBrush Shine = new LinearGradientBrush
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
        EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
        GradientStops =
        {
            new GradientStop(Color.Parse("#55FFFFFF"), 0.0),
            new GradientStop(Color.Parse("#00FFFFFF"), 0.45),
            new GradientStop(Color.Parse("#00000000"), 0.6),
            new GradientStop(Color.Parse("#40000000"), 1.0),
        },
    }.ToImmutable();

    private static readonly IPen Groove = new ImmutablePen(new ImmutableSolidColorBrush(Color.Parse("#14000000")), 1);
    private static readonly IPen Rim = new ImmutablePen(new ImmutableSolidColorBrush(Color.Parse("#40FFFFFF")), 1.2);

    static DiscGlyph()
    {
        AffectsRender<DiscGlyph>(HasDiscProperty, HoleBrushProperty, OutlineBrushProperty);
    }

    /// <summary>True while an image is loaded: draws the disc instead of the empty outline.</summary>
    public bool HasDisc
    {
        get => GetValue(HasDiscProperty);
        set => SetValue(HasDiscProperty, value);
    }

    public IBrush? HoleBrush
    {
        get => GetValue(HoleBrushProperty);
        set => SetValue(HoleBrushProperty, value);
    }

    public IBrush? OutlineBrush
    {
        get => GetValue(OutlineBrushProperty);
        set => SetValue(OutlineBrushProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        var size = Math.Min(Bounds.Width, Bounds.Height);
        if (size <= 4) return;
        var center = new Point(Bounds.Width / 2, Bounds.Height / 2);
        var r = size / 2 - 1;
        var hole = HoleBrush ?? Brushes.Transparent;

        if (!HasDisc)
        {
            var outline = OutlineBrush ?? Brushes.Gray;
            var dashed = new Pen(outline, 1.5, new DashStyle([3, 3], 0));
            context.DrawEllipse(null, dashed, center, r - 1, r - 1);
            context.DrawEllipse(null, new Pen(outline, 1.2), center, r * 0.3, r * 0.3);
            context.DrawEllipse(hole, null, center, r * 0.13, r * 0.13);
            return;
        }

        context.DrawEllipse(Sheen, null, center, r, r);
        for (var ring = 0.36; ring < 0.97; ring += 0.07)
            context.DrawEllipse(null, Groove, center, r * ring, r * ring);
        context.DrawEllipse(Shine, Rim, center, r, r);

        // Hub: a dark clamping ring around the hole.
        context.DrawEllipse(new ImmutableSolidColorBrush(Color.Parse("#E60B0A10")), new ImmutablePen(new ImmutableSolidColorBrush(Color.Parse("#30FFFFFF")), 1),
            center, r * 0.3, r * 0.3);
        context.DrawEllipse(hole, null, center, r * 0.13, r * 0.13);
    }
}
