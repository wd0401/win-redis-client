using System.Collections;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Media;

namespace WinRedisClient.App.Controls;

/// <summary>轻量折线图：直接绘制 INFO 采样序列，无第三方图表依赖。</summary>
public sealed class Sparkline : FrameworkElement
{
    public static readonly DependencyProperty ValuesProperty = DependencyProperty.Register(
        nameof(Values), typeof(IEnumerable), typeof(Sparkline),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnValuesChanged));

    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(
        nameof(Stroke), typeof(Brush), typeof(Sparkline),
        new FrameworkPropertyMetadata(Brushes.DodgerBlue, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FillProperty = DependencyProperty.Register(
        nameof(Fill), typeof(Brush), typeof(Sparkline),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    private INotifyCollectionChanged? _observed;

    public IEnumerable? Values
    {
        get => (IEnumerable?)GetValue(ValuesProperty);
        set => SetValue(ValuesProperty, value);
    }

    public Brush? Stroke
    {
        get => (Brush?)GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    public Brush? Fill
    {
        get => (Brush?)GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    private static void OnValuesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var sparkline = (Sparkline)d;
        if (sparkline._observed is not null)
            sparkline._observed.CollectionChanged -= sparkline.OnCollectionChanged;

        sparkline._observed = e.NewValue as INotifyCollectionChanged;
        if (sparkline._observed is not null)
            sparkline._observed.CollectionChanged += sparkline.OnCollectionChanged;

        sparkline.InvalidateVisual();
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => InvalidateVisual();

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);

        var width = ActualWidth;
        var height = ActualHeight;
        if (width <= 4 || height <= 4) return;

        var samples = Values?.Cast<object>()
            .Select(v => v switch
            {
                double d => d,
                float f => f,
                int i => i,
                long l => l,
                _ => double.NaN,
            })
            .Where(v => !double.IsNaN(v))
            .ToList() ?? [];

        if (samples.Count == 0)
        {
            var pen = new Pen(new SolidColorBrush(Color.FromArgb(60, 148, 163, 184)), 1)
            {
                DashStyle = new DashStyle([3, 4], 0),
            };
            pen.Freeze();
            drawingContext.DrawLine(pen, new Point(0, height / 2), new Point(width, height / 2));
            return;
        }

        const double pad = 3;
        var min = samples.Min();
        var max = samples.Max();
        var range = max - min;
        if (range < 1e-9) range = Math.Max(Math.Abs(max), 1);

        double X(int index) => samples.Count == 1
            ? pad
            : pad + index * (width - pad * 2) / (samples.Count - 1);

        double Y(double value) => height - pad - (value - min) / range * (height - pad * 2);

        if (samples.Count == 1)
        {
            var dotBrush = Stroke ?? Brushes.DodgerBlue;
            drawingContext.DrawEllipse(dotBrush, null, new Point(pad, Y(samples[0])), 2, 2);
            return;
        }

        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(new Point(X(0), Y(samples[0])), isFilled: Fill is not null, isClosed: false);
            for (var i = 1; i < samples.Count; i++)
                ctx.LineTo(new Point(X(i), Y(samples[i])), isStroked: true, isSmoothJoin: true);

            if (Fill is not null)
            {
                ctx.LineTo(new Point(X(samples.Count - 1), height), isStroked: false, isSmoothJoin: false);
                ctx.LineTo(new Point(X(0), height), isStroked: false, isSmoothJoin: false);
            }
        }

        geometry.Freeze();

        if (Fill is not null)
            drawingContext.DrawGeometry(Fill, null, geometry);

        var linePen = new Pen(Stroke ?? Brushes.DodgerBlue, 1.7)
        {
            LineJoin = PenLineJoin.Round,
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
        };
        linePen.Freeze();
        drawingContext.DrawGeometry(null, linePen, geometry);
    }
}
