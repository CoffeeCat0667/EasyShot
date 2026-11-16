using System.Windows;

namespace EasyShot.Overlay;

// 选区、标注和贴图边框共用的八方向拖拽计算；handle 从左上开始顺时针。
internal static class ResizeBounds
{
    public static Rect Calculate(Rect original, int handle, Vector delta, double minWidth, double minHeight,
        bool keepAspect, Rect? limit = null)
    {
        var leftEdge = handle is 0 or 6 or 7;
        var rightEdge = handle is 2 or 3 or 4;
        var topEdge = handle is 0 or 1 or 2;
        var bottomEdge = handle is 4 or 5 or 6;
        var width = Math.Max(1, original.Width);
        var height = Math.Max(1, original.Height);
        if (!keepAspect)
        {
            var left = leftEdge ? Math.Min(original.Left + delta.X, original.Right - minWidth) : original.Left;
            var right = rightEdge ? Math.Max(original.Right + delta.X, original.Left + minWidth) : original.Right;
            var top = topEdge ? Math.Min(original.Top + delta.Y, original.Bottom - minHeight) : original.Top;
            var bottom = bottomEdge ? Math.Max(original.Bottom + delta.Y, original.Top + minHeight) : original.Bottom;
            if (limit is { } bounds)
            {
                left = Math.Max(bounds.Left, left);
                right = Math.Min(bounds.Right, right);
                top = Math.Max(bounds.Top, top);
                bottom = Math.Min(bounds.Bottom, bottom);
            }
            return new Rect(new Point(left, top), new Point(right, bottom));
        }

        var scaleX = (width + (leftEdge ? -delta.X : delta.X)) / width;
        var scaleY = (height + (topEdge ? -delta.Y : delta.Y)) / height;
        var scale = leftEdge || rightEdge ? scaleX : scaleY;
        if ((leftEdge || rightEdge) && (topEdge || bottomEdge))
            scale = Math.Abs(scaleX - 1) >= Math.Abs(scaleY - 1) ? scaleX : scaleY;

        var anchorX = leftEdge ? 1.0 : rightEdge ? 0.0 : 0.5;
        var anchorY = topEdge ? 1.0 : bottomEdge ? 0.0 : 0.5;
        var anchor = new Point(original.Left + width * anchorX, original.Top + height * anchorY);
        var minimum = Math.Max(minWidth / width, minHeight / height);
        var maximum = double.PositiveInfinity;
        if (limit is { } area)
        {
            if (anchorX > 0) maximum = Math.Min(maximum, (anchor.X - area.Left) / (width * anchorX));
            if (anchorX < 1) maximum = Math.Min(maximum, (area.Right - anchor.X) / (width * (1 - anchorX)));
            if (anchorY > 0) maximum = Math.Min(maximum, (anchor.Y - area.Top) / (height * anchorY));
            if (anchorY < 1) maximum = Math.Min(maximum, (area.Bottom - anchor.Y) / (height * (1 - anchorY)));
        }
        scale = Math.Clamp(scale, Math.Min(minimum, maximum), maximum);
        return new Rect(anchor.X - width * scale * anchorX, anchor.Y - height * scale * anchorY,
            width * scale, height * scale);
    }
}
