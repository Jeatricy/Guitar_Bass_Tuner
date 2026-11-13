using System.Windows;
using System.Windows.Media;

namespace GuitarBassTuner.Controls;

public sealed class InputMeter : FrameworkElement
{
    private static readonly Brush Inactive = DrawingStyle.Fill(0x335986, 46);
    private double level;

    internal void SetLevel(double normalized)
    {
        if (Math.Abs(level - normalized) < 0.002)
            return;
        level = normalized;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        const int count = 14;
        const double gap = 2;
        double width = (ActualWidth - gap * (count - 1)) / count;
        if (width <= 0 || ActualHeight <= 0)
            return;

        for (int index = 0; index < count; index++)
        {
            Brush brush = level * count > index
                ? index > 11 ? DrawingStyle.Warning : DrawingStyle.Accent
                : Inactive;
            dc.DrawRoundedRectangle(brush, null, new Rect(index * (width + gap), 0, width, ActualHeight), 1.4, 1.4);
        }
    }
}
