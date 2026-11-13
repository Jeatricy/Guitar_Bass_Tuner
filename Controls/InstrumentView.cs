using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GuitarBassTuner.Controls;

internal enum Headstock { SplitGuitar, InlineGuitar, InlineBass }

public sealed class InstrumentView : FrameworkElement
{
    private static readonly Brush PegFace = DrawingStyle.Fill(0xFFFFFF);
    private static readonly Brush HighlightGlow = DrawingStyle.Fill(DrawingStyle.Blue, 70);
    private static readonly Pen Glow = DrawingStyle.Stroke(HighlightGlow, 5.0);
    private static readonly Pen SelectedPen = DrawingStyle.Stroke(DrawingStyle.Accent, 1.9);
    private static readonly Pen HoverPen = DrawingStyle.Stroke(DrawingStyle.Fill(DrawingStyle.Blue, 155), 1.4);

    private static readonly Artwork SplitGuitar = new("guitar-neck.png", 512, 605, 1140,
    [
        new(new(408, 430), 431, 425), new(new(423, 296), 462, 460), new(new(430, 162), 494, 494),
        new(new(596, 162), 525, 527), new(new(602, 296), 556, 559), new(new(615, 430), 586, 591)
    ]);
    private static readonly Artwork InlineGuitar = new("guitar-inline-neck.png", 475, 606, 1060,
    [
        new(new(414, 510), 425, 422), new(new(425, 429), 443, 441), new(new(437, 346), 462, 462),
        new(new(452, 264), 482, 483), new(new(464, 180), 501, 504), new(new(480, 97), 522, 525)
    ]);
    private static readonly Artwork InlineBass = new("bass-neck.png", 463, 683, 1120,
    [
        new(new(417, 546), 432, 428), new(new(440, 410), 464, 463),
        new(new(463, 276), 494, 497), new(new(485, 144), 524, 530)
    ]);

    private Artwork artwork = SplitGuitar;
    private int? selected;
    private int? hovered;

    public event EventHandler<int>? StringSelected;

    internal void SetHeadstock(Headstock headstock)
    {
        artwork = headstock switch
        {
            Headstock.InlineGuitar => InlineGuitar,
            Headstock.InlineBass => InlineBass,
            _ => SplitGuitar
        };
        UpdateHover(null);
        InvalidateVisual();
    }

    internal void Highlight(int? index)
    {
        if (selected == index)
            return;
        selected = index;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        var bounds = new Rect(RenderSize);
        dc.DrawRectangle(Brushes.Transparent, null, bounds);
        if (bounds.Width <= 0 || bounds.Height <= 14)
            return;

        var (scale, offset) = Placement();
        dc.PushClip(new RectangleGeometry(bounds, 13, 13));
        dc.DrawImage(artwork.Image, new Rect(offset, new Size(1024 * scale, 1536 * scale)));
        if (hovered is int hover && hover != selected)
            DrawString(dc, hover, scale, offset, false);
        if (selected is int index && index < artwork.Strings.Length)
            DrawString(dc, index, scale, offset, true);
        dc.Pop();
    }

    private (double Scale, Point Offset) Placement()
    {
        double scale = Math.Max(0.001, Math.Min((ActualHeight - 10) / artwork.VisibleHeight, ActualWidth / 650));
        double top = Math.Max(10, ActualHeight - 1536 * scale);
        return (scale, new Point(ActualWidth / 2 - artwork.Center * scale, top));
    }

    private void DrawString(DrawingContext dc, int index, double scale, Point offset, bool active)
    {
        var (peg, nut, bottom) = Points(artwork.Strings[index], scale, offset);
        Pen pen = active ? SelectedPen : HoverPen;
        if (active)
        {
            dc.DrawLine(Glow, peg, nut);
            dc.DrawLine(Glow, nut, bottom);
        }
        dc.DrawLine(pen, peg, nut);
        dc.DrawLine(pen, nut, bottom);
        dc.DrawEllipse(PegFace, pen, peg, 5.3, 5.3);
        if (active)
            dc.DrawEllipse(DrawingStyle.Accent, null, peg, 2.1, 2.1);
    }

    private (Point Peg, Point Nut, Point Bottom) Points(StringPath path, double scale, Point offset) =>
        (Place(path.Peg.X, path.Peg.Y, scale, offset),
         Place(path.NutX, artwork.Nut, scale, offset),
         Place(path.BottomX, 1536, scale, offset));

    private static Point Place(double x, double y, double scale, Point offset) => new(offset.X + x * scale, offset.Y + y * scale);

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        UpdateHover(FindString(e.GetPosition(this)));
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        UpdateHover(null);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (FindString(e.GetPosition(this)) is int index)
        {
            StringSelected?.Invoke(this, index);
            e.Handled = true;
        }
    }

    private void UpdateHover(int? index)
    {
        if (hovered == index)
            return;
        hovered = index;
        Cursor = index.HasValue ? Cursors.Hand : Cursors.Arrow;
        InvalidateVisual();
    }

    private int? FindString(Point point)
    {
        var (scale, offset) = Placement();
        if (point.Y < offset.Y)
            return null;

        int? closest = null;
        double distance = 13 * 13;
        for (int index = 0; index < artwork.Strings.Length; index++)
        {
            var (peg, nut, bottom) = Points(artwork.Strings[index], scale, offset);
            double candidate = Math.Min(DistanceSquared(point, peg, nut), DistanceSquared(point, nut, bottom));
            if (candidate < distance)
            {
                closest = index;
                distance = candidate;
            }
        }
        return closest;
    }

    private static double DistanceSquared(Point point, Point start, Point end)
    {
        Vector segment = end - start;
        double fraction = Math.Clamp(Vector.Multiply(point - start, segment) / segment.LengthSquared, 0, 1);
        return (point - (start + segment * fraction)).LengthSquared;
    }

    private readonly record struct StringPath(Point Peg, double NutX, double BottomX);

    private sealed class Artwork(string resource, double center, double nut, double visibleHeight, StringPath[] strings)
    {
        public double Center { get; } = center;
        public double Nut { get; } = nut;
        public double VisibleHeight { get; } = visibleHeight;
        public StringPath[] Strings { get; } = strings;
        public BitmapImage Image { get; } = LoadImage(resource);

        private static BitmapImage LoadImage(string name)
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.UriSource = new Uri($"pack://application:,,,/GuitarBassTuner;component/Assets/{name}", UriKind.Absolute);
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.EndInit();
            image.Freeze();
            return image;
        }
    }
}
