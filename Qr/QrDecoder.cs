using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ZXing;
using ZXing.Common;

namespace EasyShot.Qr;

internal sealed record CodeResult(string Format, string Text);

internal static class QrDecoder
{
    public static IReadOnlyList<CodeResult> Decode(BitmapSource image)
    {
        BitmapSource bitmap = image.Format == PixelFormats.Gray8 ? image
            : new FormatConvertedBitmap(image, PixelFormats.Gray8, null, 0);
        var reader = new BarcodeReaderGeneric
        {
            AutoRotate = true,
            Options = new DecodingOptions
            {
                TryHarder = true,
                CharacterSet = "UTF-8",
                PossibleFormats = new[] { BarcodeFormat.QR_CODE, BarcodeFormat.CODE_128, BarcodeFormat.CODE_39,
                    BarcodeFormat.EAN_13, BarcodeFormat.EAN_8, BarcodeFormat.UPC_A, BarcodeFormat.UPC_E,
                    BarcodeFormat.ITF, BarcodeFormat.DATA_MATRIX, BarcodeFormat.AZTEC, BarcodeFormat.PDF_417 }.ToList()
            }
        };
        // 原分辨率扫描。重叠至少一倍图宽，避免跨分块边界的方形码被截断。
        var blockHeight = Math.Max(2048, checked(bitmap.PixelWidth * 2));
        var overlap = Math.Max(512, bitmap.PixelWidth);
        var found = new List<(CodeResult Code, Rect Bounds)>();
        for (var top = 0; top < bitmap.PixelHeight; top += blockHeight - overlap)
        {
            var height = Math.Min(blockHeight, bitmap.PixelHeight - top);
            var pixels = new byte[checked(bitmap.PixelWidth * height)];
            bitmap.CopyPixels(new Int32Rect(0, top, bitmap.PixelWidth, height), pixels, bitmap.PixelWidth, 0);
            var source = new PlanarYUVLuminanceSource(pixels, bitmap.PixelWidth, height,
                0, 0, bitmap.PixelWidth, height, false);
            var results = reader.DecodeMultiple(source);
            if (results is not null)
            {
                foreach (var result in results)
                {
                    var bounds = Rect.Empty;
                    foreach (var point in result.ResultPoints ?? Array.Empty<ResultPoint>())
                        if (point is not null) bounds.Union(new Point(point.X, point.Y + top));
                    if (!bounds.IsEmpty)
                    {
                        // 一维码结果点可能共线，给位置比较保留最小面积。
                        bounds = new Rect(bounds.X, bounds.Y, Math.Max(1, bounds.Width), Math.Max(1, bounds.Height));
                    }
                    var code = new CodeResult(result.BarcodeFormat.ToString(), result.Text);
                    if (found.Any(previous => previous.Code == code && SamePosition(previous.Bounds, bounds))) continue;
                    found.Add((code, bounds));
                }
            }
            if (top + height == bitmap.PixelHeight) break;
        }
        return found.OrderBy(item => item.Bounds.IsEmpty ? 0 : item.Bounds.Y)
            .ThenBy(item => item.Bounds.IsEmpty ? 0 : item.Bounds.X).Select(item => item.Code).ToArray();
    }

    private static bool SamePosition(Rect first, Rect second)
    {
        if (first.IsEmpty || second.IsEmpty) return false;
        var intersection = Rect.Intersect(first, second);
        return !intersection.IsEmpty && intersection.Width * intersection.Height >=
            Math.Min(first.Width * first.Height, second.Width * second.Height) * 0.5;
    }
}
