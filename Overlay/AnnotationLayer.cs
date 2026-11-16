using System.Windows;
using System.Windows.Media;

namespace EasyShot.Overlay;

internal sealed class AnnotationLayer : FrameworkElement
{
    private readonly OverlaySession _session;
    private readonly OverlayWindow _window;

    public AnnotationLayer(OverlaySession session, OverlayWindow window)
    {
        _session = session;
        _window = window;
        IsHitTestVisible = false;
    }

    protected override void OnRender(DrawingContext dc)
    {
        var selection = _session.Selection;
        if (selection is not { } rect)
        {
            return;
        }

        var origin = _window.ToLocalDip(rect);
        dc.PushTransform(new TranslateTransform(origin.X, origin.Y));
        dc.PushTransform(new ScaleTransform(1 / _window.Scale, 1 / _window.Scale));
        dc.PushClip(new RectangleGeometry(new Rect(0, 0, rect.Width, rect.Height)));
        _session.RenderAnnotations(dc, true);
        dc.Pop();
        dc.Pop();
        dc.Pop();
    }
}
