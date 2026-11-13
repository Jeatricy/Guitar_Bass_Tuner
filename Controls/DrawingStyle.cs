using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace GuitarBassTuner.Controls;

internal static class DrawingStyle
{
    public const uint Blue = 0x0A84FF;
    public const uint Green = 0x137646;
    public static readonly Brush Accent = Fill(Blue);
    public static readonly Brush AccentInk = Fill(0x0059BE);
    public static readonly Brush InTune = Fill(Green);
    public static readonly Brush Warning = Fill(0xA85B00);
    public static readonly Brush Muted = Fill(0x4E5869);
    public static readonly Typeface Ui = new(new FontFamily("Segoe UI Variable Display, Segoe UI, Microsoft YaHei UI"),
        FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
    public static readonly Typeface Mono = new(new FontFamily("Consolas, Segoe UI"), FontStyles.Normal,
        FontWeights.Normal, FontStretches.Normal);
    private static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("zh-CN");

    public static SolidColorBrush Fill(uint rgb, byte opacity = 255)
    {
        var brush = new SolidColorBrush(Color.FromArgb(opacity, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb));
        brush.Freeze();
        return brush;
    }

    public static Pen Stroke(Brush brush, double width)
    {
        var pen = new Pen(brush, width) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        pen.Freeze();
        return pen;
    }

    public static FormattedText Label(string text, Typeface typeface, double size, Brush brush, double dpi) =>
        new(text, Culture, FlowDirection.LeftToRight, typeface, size, brush, dpi);
}
