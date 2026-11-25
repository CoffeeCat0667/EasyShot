using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Globalization;

namespace EasyShot.Overlay;

internal enum AnnotationTool
{
    Select,
    Rectangle,
    Arrow,
    Pen,
    Text,
    Number,
    Mosaic
}

internal sealed class Annotation
{
    public AnnotationTool Tool { get; init; }
    public Color Color { get; set; }
    public double Thickness { get; set; }
    public List<Point> Points { get; } = new();
    public string Text { get; set; } = "";
    public int Number { get; set; }

    public Rect Bounds
    {
        get
        {
            var bounds = Rect.Empty;
            foreach (var point in Points)
            {
                bounds.Union(point);
            }

            return bounds;
        }
    }

    public Annotation Clone()
    {
        var copy = new Annotation { Tool = Tool, Color = Color, Thickness = Thickness, Text = Text, Number = Number };
        copy.Points.AddRange(Points);
        return copy;
    }

    public Rect SelectionBounds
    {
        get
        {
            var bounds = Bounds;
            bounds.Inflate(Thickness / 2 + 4, Thickness / 2 + 4);
            return bounds;
        }
    }

    public Point[] HandleCenters()
    {
        var bounds = SelectionBounds;
        var centerX = bounds.X + bounds.Width / 2;
        var centerY = bounds.Y + bounds.Height / 2;
        return new[]
        {
            bounds.TopLeft, new Point(centerX, bounds.Top), bounds.TopRight,
            new Point(bounds.Right, centerY), bounds.BottomRight,
            new Point(centerX, bounds.Bottom), bounds.BottomLeft,
            new Point(bounds.Left, centerY)
        };
    }

    public void ResizeFrom(Annotation original, Rect frame)
    {
        var padding = original.Thickness / 2 + 4;
        frame.Inflate(-padding, -padding);
        var bounds = original.Bounds;
        Points.Clear();
        for (var i = 0; i < original.Points.Count; i++)
        {
            var point = original.Points[i];
            var x = bounds.Width > 0 ? (point.X - bounds.X) / bounds.Width
                : Tool == AnnotationTool.Arrow ? i / (double)(original.Points.Count - 1) : 0.5;
            var y = bounds.Height > 0 ? (point.Y - bounds.Y) / bounds.Height
                : Tool == AnnotationTool.Arrow ? i / (double)(original.Points.Count - 1) : 0.5;
            Points.Add(new Point(frame.X + x * frame.Width, frame.Y + y * frame.Height));
        }
    }

    public void Move(Vector delta)
    {
        for (var i = 0; i < Points.Count; i++)
        {
            Points[i] += delta;
        }
    }

    public void Render(DrawingContext dc, BitmapSource screen, Int32Rect virtualScreen)
    {
        if (Points.Count < 2)
        {
            return;
        }

        var brush = new SolidColorBrush(Color);
        var pen = new Pen(brush, Thickness)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round
        };

        if (Tool == AnnotationTool.Text || Tool == AnnotationTool.Number)
        {
            var rect = new Rect(Points[0], Points[^1]);
            if (rect.Width <= 0 || rect.Height <= 0) return;
            if (Tool == AnnotationTool.Number) dc.DrawEllipse(null, pen, new Point(rect.X + rect.Width / 2, rect.Y + rect.Height / 2), rect.Width / 2, rect.Height / 2);
            var text = new FormattedText(Tool == AnnotationTool.Number ? Number.ToString() : Text,
                CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Microsoft YaHei"),
                Tool == AnnotationTool.Number ? 20 : 24, brush, 1);
            if (text.Width <= 0 || text.Height <= 0) return;
            dc.PushTransform(new TranslateTransform(rect.X, rect.Y));
            if (Tool == AnnotationTool.Number)
            {
                var scale = Math.Min(rect.Width * 0.65 / text.WidthIncludingTrailingWhitespace, rect.Height * 0.65 / text.Height);
                dc.PushTransform(new ScaleTransform(scale, scale));
                dc.DrawText(text, new Point((rect.Width / scale - text.WidthIncludingTrailingWhitespace) / 2, (rect.Height / scale - text.Height) / 2));
            }
            else
            {
                dc.PushTransform(new ScaleTransform(rect.Width / text.WidthIncludingTrailingWhitespace, rect.Height / text.Height));
                dc.DrawText(text, new Point(0, 0));
            }
            dc.Pop();
            dc.Pop();
        }
        else if (Tool == AnnotationTool.Mosaic)
        {
            var rect = new Rect(Points[0], Points[^1]);
            var x1 = Math.Max(virtualScreen.X, (int)Math.Floor(rect.Left));
            var y1 = Math.Max(virtualScreen.Y, (int)Math.Floor(rect.Top));
            var x2 = Math.Min(virtualScreen.X + virtualScreen.Width, (int)Math.Ceiling(rect.Right));
            var y2 = Math.Min(virtualScreen.Y + virtualScreen.Height, (int)Math.Ceiling(rect.Bottom));
            if (x2 <= x1 || y2 <= y1) return;
            var crop = new CroppedBitmap(screen, new Int32Rect(x1 - virtualScreen.X, y1 - virtualScreen.Y, x2 - x1, y2 - y1));
            var scale = new ScaleTransform(1.0 / 12, 1.0 / 12);
            var small = new TransformedBitmap(crop, scale);
            var visual = new DrawingVisual();
            RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.NearestNeighbor);
            using (var drawing = visual.RenderOpen()) drawing.DrawImage(small, new Rect(x1, y1, x2 - x1, y2 - y1));
            dc.DrawDrawing(visual.Drawing);
        }
        else if (Tool == AnnotationTool.Rectangle)
        {
            dc.DrawRectangle(null, pen, new Rect(Points[0], Points[^1]));
        }
        else if (Tool == AnnotationTool.Arrow)
        {
            var start = Points[0];
            var end = Points[^1];
            dc.DrawLine(pen, start, end);
            var direction = end - start;
            if (direction.Length > 0)
            {
                direction.Normalize();
                var length = Math.Max(12, Thickness * 4);
                var normal = new Vector(-direction.Y, direction.X);
                dc.DrawLine(pen, end, end - direction * length + normal * length * 0.45);
                dc.DrawLine(pen, end, end - direction * length - normal * length * 0.45);
            }
        }
        else
        {
            var geometry = new StreamGeometry();
            using (var context = geometry.Open())
            {
                context.BeginFigure(Points[0], false, false);
                context.PolyLineTo(Points.Skip(1).ToArray(), true, false);
            }

            dc.DrawGeometry(null, pen, geometry);
        }
    }

    public bool HitTest(Point point)
    {
        if (Points.Count < 2)
        {
            return false;
        }

        var tolerance = Math.Max(6, Thickness / 2 + 4);
        if (Tool is AnnotationTool.Text or AnnotationTool.Number or AnnotationTool.Mosaic)
            return new Rect(Points[0], Points[^1]).Contains(point);
        if (Tool == AnnotationTool.Rectangle)
        {
            var rect = new Rect(Points[0], Points[^1]);
            var outer = rect;
            outer.Inflate(tolerance, tolerance);
            var inner = rect;
            inner.Inflate(-tolerance, -tolerance);
            return outer.Contains(point) && (inner.IsEmpty || !inner.Contains(point));
        }

        for (var i = 1; i < Points.Count; i++)
        {
            var segment = Points[i] - Points[i - 1];
            var lengthSquared = segment.LengthSquared;
            var t = lengthSquared == 0 ? 0 : Math.Clamp(Vector.Multiply(point - Points[i - 1], segment) / lengthSquared, 0, 1);
            if ((point - (Points[i - 1] + segment * t)).Length <= tolerance)
            {
                return true;
            }
        }

        if (Tool == AnnotationTool.Arrow)
        {
            return (point - Points[^1]).Length <= Math.Max(12, Thickness * 4);
        }

        return false;
    }
}
