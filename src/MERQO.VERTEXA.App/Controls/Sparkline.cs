using System.Windows;
using System.Windows.Media;

namespace Merqo.Vertexa.App.Controls;

/// <summary>
/// Minimal, DPI-correct sparkline. Draws only samples that actually exist: gaps in the
/// data are rendered as gaps, never interpolated into a flat line.
/// </summary>
public sealed class Sparkline : FrameworkElement
{
    public static readonly DependencyProperty ValuesProperty = DependencyProperty.Register(
        nameof(Values), typeof(IReadOnlyList<double?>), typeof(Sparkline),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(
        nameof(Stroke), typeof(Brush), typeof(Sparkline),
        new FrameworkPropertyMetadata(Brushes.DeepSkyBlue, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(
        nameof(Maximum), typeof(double), typeof(Sparkline),
        new FrameworkPropertyMetadata(100.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public IReadOnlyList<double?>? Values
    {
        get => (IReadOnlyList<double?>?)GetValue(ValuesProperty);
        set => SetValue(ValuesProperty, value);
    }

    public Brush Stroke
    {
        get => (Brush)GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    public double Maximum
    {
        get => (double)GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        var width = ActualWidth;
        var height = ActualHeight;
        if (width <= 2 || height <= 2) return;

        var background = new DrawingGroup();
        var bounds = new Rect(0, 0, width, height);
        drawingContext.DrawRectangle(null, new Pen(new SolidColorBrush(Color.FromArgb(40, 120, 140, 160)), 0.5), bounds);

        var values = Values;
        if (values is null || values.Count < 2)
        {
            var typeface = new Typeface("Segoe UI");
            var text = new FormattedText("no samples", System.Globalization.CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, typeface, 10, new SolidColorBrush(Color.FromArgb(120, 160, 175, 190)),
                VisualTreeHelper.GetDpi(this).PixelsPerDip);
            drawingContext.DrawText(text, new Point(6, (height - text.Height) / 2));
            return;
        }

        var pen = new Pen(Stroke, 1.4) { LineJoin = PenLineJoin.Round };
        var step = width / Math.Max(1, values.Count - 1);
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            var open = false;
            for (var i = 0; i < values.Count; i++)
            {
                var value = values[i];
                if (value is null)
                {
                    open = false;
                    continue;
                }
                var normalised = Math.Clamp(value.Value / Math.Max(Maximum, 0.0001), 0, 1);
                var point = new Point(i * step, height - (normalised * (height - 4)) - 2);
                if (!open)
                {
                    context.BeginFigure(point, false, false);
                    open = true;
                }
                else
                {
                    context.LineTo(point, true, false);
                }
            }
        }
        geometry.Freeze();
        drawingContext.DrawGeometry(null, pen, geometry);
        _ = background;
    }
}
