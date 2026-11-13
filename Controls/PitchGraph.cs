using System.Diagnostics;
using System.Windows;
using System.Windows.Media;

namespace GuitarBassTuner.Controls;

public sealed class PitchGraph : FrameworkElement
{
    private const double Duration = 8;
    internal const double InTuneCents = 3;
    private const double MaximumSampleGap = 0.16;
    private static readonly Brush Background = DrawingStyle.Fill(0xFFFFFF, 173);
    private static readonly Brush PromptBrush = DrawingStyle.Fill(0xFFFFFF, 242);
    private static readonly Brush TargetBand = DrawingStyle.Fill(0x34C759, 32);
    private static readonly Pen Frame = DrawingStyle.Stroke(DrawingStyle.Fill(0xFFFFFF, 230), 1);
    private static readonly Pen GridLine = DrawingStyle.Stroke(DrawingStyle.Fill(0x2E5280, 39), 1);
    private static readonly Pen ZeroLine = DrawingStyle.Stroke(DrawingStyle.Fill(DrawingStyle.Green, 139), 1);
    private static readonly Pen Trace = DrawingStyle.Stroke(DrawingStyle.Accent, 1.7);
    private static readonly Pen TraceGlow = DrawingStyle.Stroke(DrawingStyle.Fill(DrawingStyle.Blue, 34), 4.6);
    private static readonly Pen CurrentLine = DrawingStyle.Stroke(DrawingStyle.Fill(0x0A62BB, 110), 1);
    private static readonly Pen CurrentTick = DrawingStyle.Stroke(DrawingStyle.Fill(0x0A62BB, 113), 1);
    private static readonly string[] TimeLabels = ["−8 s", "−6 s", "−4 s", "−2 s", "现在"];
    private static readonly double[] Scales = [50, 100, 200, 400, 800, 1600, 3200, 6400];
    private readonly List<PitchPoint> points = new(300);
    private long now = Stopwatch.GetTimestamp();
    private double? pointer;
    private string prompt = "拨响一根空弦";

    internal void Push(long timestamp, double cents, int stringIndex) => points.Add(new(timestamp, cents, stringIndex));

    internal void Reset()
    {
        points.Clear();
        pointer = null;
        InvalidateVisual();
    }

    internal void Update(long timestamp, double? cents, string message)
    {
        if (points.Count == 0 && pointer == cents && prompt == message)
            return;

        now = timestamp;
        pointer = cents;
        prompt = message;
        int expired = 0;
        while (expired < points.Count && Age(points[expired].Timestamp) > Duration + MaximumSampleGap)
            expired++;
        if (expired > 0)
            points.RemoveRange(0, expired);
        InvalidateVisual();
    }

    private double Age(long timestamp) => Stopwatch.GetElapsedTime(timestamp, now).TotalSeconds;

    protected override void OnRender(DrawingContext dc)
    {
        const double leftGutter = 58;
        const double rightGutter = 37;
        double width = ActualWidth - leftGutter - rightGutter;
        double height = ActualHeight - 67;
        if (width <= 0 || height <= 0)
            return;

        var bounds = new Rect(0.5, 0.5, ActualWidth - 1, ActualHeight - 1);
        var plot = new Rect(leftGutter, 28, width, height);
        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        double scale = GetScale();
        dc.DrawRoundedRectangle(Background, Frame, bounds, 14, 14);
        DrawAxes(dc, plot, scale, dpi);
        DrawHistory(dc, plot, scale);
        DrawPointer(dc, plot, scale);

        if (points.Count == 0 && pointer is null && prompt.Length > 0)
        {
            var text = DrawingStyle.Label(prompt, DrawingStyle.Ui, 15, DrawingStyle.Muted, dpi);
            var center = new Point(plot.Left + plot.Width / 2, plot.Top + plot.Height / 2);
            var capsule = new Rect(center.X - text.Width / 2 - 12, center.Y - 15, text.Width + 24, 30);
            dc.DrawRoundedRectangle(PromptBrush, Frame, capsule, 10, 10);
            dc.DrawText(text, new Point(center.X - text.Width / 2, center.Y - text.Height / 2));
        }
    }

    private double GetScale()
    {
        double maximum = Math.Abs(pointer ?? 0);
        foreach (PitchPoint point in points)
        {
            if (Age(point.Timestamp) <= Duration)
                maximum = Math.Max(maximum, Math.Abs(point.Cents));
        }
        foreach (double scale in Scales)
        {
            if (maximum <= scale)
                return scale;
        }
        return Scales[^1];
    }

    private static double Y(Rect plot, double scale, double cents) =>
        plot.Top + (1 - Math.Clamp(cents / scale, -1, 1)) * plot.Height / 2;

    private static void DrawAxes(DrawingContext dc, Rect plot, double scale, double dpi)
    {
        double middle = Y(plot, scale, 0);
        double band = plot.Height * InTuneCents / scale;
        dc.DrawRectangle(TargetBand, null, new Rect(plot.Left, middle - band / 2, plot.Width, band));
        for (int step = 0; step < TimeLabels.Length; step++)
        {
            double x = plot.Left + plot.Width * step / (TimeLabels.Length - 1);
            dc.DrawLine(GridLine, new Point(x, plot.Top), new Point(x, plot.Bottom));
            bool live = step == TimeLabels.Length - 1;
            var label = DrawingStyle.Label(TimeLabels[step], live ? DrawingStyle.Ui : DrawingStyle.Mono, 11,
                live ? DrawingStyle.AccentInk : DrawingStyle.Muted, dpi);
            dc.DrawText(label, new Point(x - label.Width / 2, plot.Bottom + 11));
        }

        for (int step = -2; step <= 2; step++)
        {
            double value = step * scale / 2;
            double y = Y(plot, scale, value);
            dc.DrawLine(step == 0 ? ZeroLine : GridLine, new Point(plot.Left, y), new Point(plot.Right, y));
            string number = value.ToString("+0;−0;0");
            var label = DrawingStyle.Label(number, DrawingStyle.Mono, 11, DrawingStyle.Muted, dpi);
            dc.DrawText(label, new Point(plot.Left - label.Width - 10, y - label.Height / 2));
        }
    }

    private void DrawHistory(DrawingContext dc, Rect plot, double scale)
    {
        if (points.Count == 0)
            return;

        var geometry = new StreamGeometry();
        var singleSamples = new List<Point>();
        using (StreamGeometryContext path = geometry.Open())
        {
            PitchPoint previous = default;
            Point last = default;
            int segmentLength = 0;
            foreach (PitchPoint sample in points)
            {
                double x = plot.Right - Age(sample.Timestamp) * plot.Width / Duration;
                var point = new Point(x, Y(plot, scale, sample.Cents));
                bool connected = segmentLength > 0 && sample.StringIndex == previous.StringIndex &&
                    Stopwatch.GetElapsedTime(previous.Timestamp, sample.Timestamp).TotalSeconds < MaximumSampleGap;
                if (!connected)
                {
                    if (segmentLength == 1)
                        singleSamples.Add(last);
                    path.BeginFigure(point, false, false);
                    segmentLength = 1;
                }
                else
                {
                    path.LineTo(point, true, true);
                    segmentLength++;
                }
                last = point;
                previous = sample;
            }
            if (segmentLength == 1)
                singleSamples.Add(last);
        }
        geometry.Freeze();

        dc.PushClip(new RectangleGeometry(new Rect(plot.X, plot.Y - 2, plot.Width, plot.Height + 4)));
        dc.DrawGeometry(null, TraceGlow, geometry);
        dc.DrawGeometry(null, Trace, geometry);
        foreach (Point point in singleSamples)
            dc.DrawEllipse(DrawingStyle.Accent, null, point, 2, 2);
        dc.Pop();
    }

    private void DrawPointer(DrawingContext dc, Rect plot, double scale)
    {
        dc.DrawLine(CurrentLine, new Point(plot.Right, plot.Top), new Point(plot.Right, plot.Bottom));
        for (int tick = 0; tick <= 20; tick++)
        {
            double y = plot.Top + tick * plot.Height / 20;
            int length = tick % 5 == 0 ? 6 : 3;
            dc.DrawLine(CurrentTick, new Point(plot.Right + 1, y), new Point(plot.Right + length, y));
        }
        if (pointer is not double cents)
            return;

        double pitchY = Y(plot, scale, cents);
        double x = plot.Right;
        Brush brush = Math.Abs(cents) <= InTuneCents ? DrawingStyle.InTune : DrawingStyle.Warning;
        dc.DrawEllipse(brush, null, new Point(x, pitchY), 3.4, 3.4);
        var arrow = new StreamGeometry();
        using (StreamGeometryContext path = arrow.Open())
        {
            path.BeginFigure(new Point(x + 5, pitchY), true, true);
            path.LineTo(new Point(x + 13, pitchY - 6), true, false);
            path.LineTo(new Point(x + 24, pitchY - 6), true, false);
            path.LineTo(new Point(x + 24, pitchY + 6), true, false);
            path.LineTo(new Point(x + 13, pitchY + 6), true, false);
        }
        dc.DrawGeometry(brush, null, arrow);
    }

    private readonly record struct PitchPoint(long Timestamp, double Cents, int StringIndex);
}
