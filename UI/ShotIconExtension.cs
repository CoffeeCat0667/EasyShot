using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Shapes;

namespace EasyShot.UI;

// 本项目工具栏与托盘使用的 24px 线性图标，不依赖图标字体或第三方 UI 包。
public sealed class ShotIconExtension : MarkupExtension
{
    public string Name { get; set; }
    public ShotIconExtension(string name) => Name = name;

    public override object ProvideValue(IServiceProvider serviceProvider) => Create(Name);

    public static FrameworkElement Create(string name)
    {
        var data = name switch
        {
            "Select" => "M5,3 L5,20 9,15 13,21 16,19 12,13 19,12 Z",
            "Rectangle" => "M4,5 H20 V19 H4 Z",
            "Arrow" => "M5,19 L19,5 M10,5 H19 V14",
            "Pen" => "M4,20 L5,15 16,4 Q17,3 18,4 L20,6 Q21,7 20,8 L9,19 Z M14,6 L18,10 M5,15 L9,19",
            "Text" => "M5,5 H19 M12,5 V20 M8,20 H16 M5,5 V8 M19,5 V8",
            "Number" => "M12,3 A9,9 0 1 1 11.99,3 M9,9 L12,7 V17 M9,17 H15",
            "Mosaic" => "M4,4 H10 V10 H4 Z M14,4 H20 V10 H14 Z M4,14 H10 V20 H4 Z M14,14 H20 V20 H14 Z",
            "Undo" => "M9,5 L4,10 9,15 M4,10 H14 Q20,10 20,16 V19",
            "Redo" => "M15,5 L20,10 15,15 M20,10 H10 Q4,10 4,16 V19",
            "Ocr" => "M3,8 V3 H8 M16,3 H21 V8 M21,16 V21 H16 M8,21 H3 V16 M7,10 H17 M7,14 H15",
            "Qr" => "M3,3 H9 V9 H3 Z M15,3 H21 V9 H15 Z M3,15 H9 V21 H3 Z M15,15 H18 V18 H21 V21 H15 Z M21,13 V15 M12,3 V7 M3,12 H7 M12,12 H14",
            "Confirm" => "M4,12 L9,17 20,6",
            "Save" => "M4,3 H17 L21,7 V21 H3 V3 Z M7,3 V10 H17 V3 M7,21 V14 H17 V21",
            "Pin" => "M8,3 H16 M9,3 V9 L6,13 V15 H18 V13 L15,9 V3 M12,15 V22",
            "Scroll" => "M7,3 H17 V21 H7 Z M10,7 L12,5 14,7 M12,5 V11 M10,17 L12,19 14,17 M12,13 V19",
            "Close" => "M6,6 L18,18 M18,6 L6,18",
            "Region" => "M3,9 V3 H9 M15,3 H21 V9 M21,15 V21 H15 M9,21 H3 V15 M7,7 H17 V17 H7 Z",
            "Fullscreen" => "M3,4 H21 V17 H3 Z M8,21 H16 M12,17 V21",
            "Settings" => "M12,8 A4,4 0 1 1 11.99,8 M10,2 H14 L15,5 18,6 21,6 23,10 21,12 21,15 20,18 17,18 14,20 13,23 H9 L8,20 5,18 2,18 1,14 3,12 3,9 4,6 7,6 9,4 Z",
            "Power" => "M12,2 V12 M6,5 A9,9 0 1 0 18,5",
            "Folder" => "M3,6 H10 L12,8 H21 V20 H3 Z M3,6 V4 H9 L11,6",
            "ZoomIn" => "M10,3 A7,7 0 1 1 9.99,3 M15,15 L21,21 M6,10 H14 M10,6 V14",
            "ZoomOut" => "M10,3 A7,7 0 1 1 9.99,3 M15,15 L21,21 M6,10 H14",
            "Expand" => "M3,9 V3 H9 M15,3 H21 V9 M21,15 V21 H15 M9,21 H3 V15",
            "Shrink" => "M3,9 H9 V3 M15,3 V9 H21 M21,15 H15 V21 M9,21 V15 H3",
            _ => "M5,5 H19 V19 H5 Z"
        };
        var path = new Path
        {
            Data = Geometry.Parse(data), StrokeThickness = 1.7, StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round
        };
        path.SetBinding(Shape.StrokeProperty, new Binding("Foreground")
        {
            RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(Control), 1)
        });
        var canvas = new Canvas { Width = 24, Height = 24, IsHitTestVisible = false };
        canvas.Children.Add(path);
        return new Viewbox { Width = 19, Height = 19, Child = canvas, IsHitTestVisible = false };
    }
}
