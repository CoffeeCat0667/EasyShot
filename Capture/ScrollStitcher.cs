using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace EasyShot.Capture;

internal enum StitchResult { Unchanged, Appended, Failed, HeightLimit }

internal sealed class ScrollStitcher
{
    public const int MaximumHeight = 20000;
    private readonly List<BitmapSource> _strips = new();
    private BitmapSource _lastFrame;
    private byte[] _lastPixels;
    private long _position;
    private long _minimumPosition;
    private long _maximumPosition;
    private int? _headerHeight;
    private int? _footerHeight;
    public string FailureReason { get; private set; } = "";
    public int Width { get; }
    public int Height { get; private set; }

    public ScrollStitcher(BitmapSource first)
    {
        _lastFrame = ToBgra(first);
        _lastPixels = Pixels(_lastFrame);
        Width = first.PixelWidth;
        Height = Math.Min(first.PixelHeight, MaximumHeight);
        _strips.Add(CopyStrip(_lastFrame, 0, Height));
    }

    public static BitmapSource ToBgra(BitmapSource image)
    {
        var converted = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
        converted.Freeze();
        return converted;
    }

    public static byte[] Pixels(BitmapSource image)
    {
        var pixels = new byte[checked(image.PixelWidth * image.PixelHeight * 4)];
        image.CopyPixels(pixels, image.PixelWidth * 4, 0);
        return pixels;
    }

    public static double Difference(byte[] first, byte[] second)
    {
        if (first.Length != second.Length) return double.MaxValue;
        long total = 0;
        for (var i = 0; i < first.Length; i += 4)
            total += Math.Abs(first[i] - second[i]) + Math.Abs(first[i + 1] - second[i + 1]) + Math.Abs(first[i + 2] - second[i + 2]);
        return total / (first.Length / 4.0 * 3);
    }

    public static bool IsStable(byte[] first, byte[] second)
    {
        if (first.Length != second.Length) return false;
        var changed = 0;
        var count = 0;
        for (var i = 0; i < first.Length; i += 16)
        {
            if (Math.Abs(first[i] - second[i]) + Math.Abs(first[i + 1] - second[i + 1]) +
                Math.Abs(first[i + 2] - second[i + 2]) > 30) changed++;
            count++;
        }
        return count > 0 && changed / (double)count < 0.003;
    }

    public StitchResult Append(BitmapSource image)
    {
        FailureReason = "";
        if (Height >= MaximumHeight) return StitchResult.HeightLimit;
        if (image.PixelWidth != Width || image.PixelHeight != _lastFrame.PixelHeight)
            return Fail("捕捉范围的像素尺寸发生变化");
        var frame = ToBgra(image);
        var pixels = Pixels(frame);
        if (IsStable(_lastPixels, pixels)) return StitchResult.Unchanged;

        var frameHeight = frame.PixelHeight;
        var content = FindScrollingContent(pixels, frameHeight);
        var top = (int)content.Top;
        var bottom = (int)content.Bottom;
        var left = (int)content.Left;
        var right = (int)content.Right;
        var contentHeight = bottom - top;
        var minOverlap = Math.Min(contentHeight - 1, Math.Max(16, contentHeight / 25));
        const int samples = 48;
        var candidates = new List<(int Shift, double Score)>();
        var previousRows = RowSamples(_lastPixels, Width, frameHeight, left, right);
        var currentRows = RowSamples(pixels, Width, frameHeight, left, right);
        // 正位移表示向下浏览，负位移表示向上；比较两帧共同的正文范围。
        for (var shift = -(contentHeight - minOverlap); shift <= contentHeight - minOverlap; shift++)
        {
            if (shift == 0) continue;
            var overlap = contentHeight - Math.Abs(shift);
            long error = 0;
            var count = 0;
            var rowStep = Math.Max(1, overlap / 96);
            for (var y = top + Math.Max(0, -shift); y < bottom - Math.Max(0, shift); y += rowStep)
                for (var sample = 0; sample < samples; sample++)
                {
                    error += Math.Abs(previousRows[(y + shift) * samples + sample] - currentRows[y * samples + sample]);
                    count++;
                }
            var score = error / (double)count;
            if (score < 12) candidates.Add((shift, score));
        }

        // 空白列可能使行特征出现多个相似候选，先做更密集的 RGB 校验再逐像素确认。
        var bestShift = 0;
        var verifiedScore = double.MaxValue;
        var verified = new List<(int Shift, double Score)>();
        // 分散保留候选：同一位移附近的一串近似结果不能挤掉其他真正重叠的位置。
        var shortlist = new List<(int Shift, double Score)>();
        foreach (var group in candidates.GroupBy(c => Math.Sign(c.Shift)))
        {
            var selected = new List<(int Shift, double Score)>();
            foreach (var candidate in group.OrderBy(c => c.Score))
            {
                if (selected.Any(c => Math.Abs(c.Shift - candidate.Shift) <= 2)) continue;
                selected.Add(candidate);
                if (selected.Count == 8) break;
            }
            shortlist.AddRange(selected);
        }
        foreach (var candidate in shortlist
                     .Select(c => (c.Shift, Score: OverlapDifference(pixels, content, c.Shift, true)))
                     .GroupBy(c => Math.Sign(c.Shift))
                     .SelectMany(group => group.OrderBy(c => c.Score).Take(2)))
        {
            if (candidate.Score >= 8) continue;
            var score = OverlapDifference(pixels, content, candidate.Shift, false);
            if (score < 8) verified.Add((candidate.Shift, score));
            if (score < 8 && score < verifiedScore)
            {
                verifiedScore = score;
                bestShift = candidate.Shift;
            }
        }
        if (bestShift == 0)
        {
            // 平滑滚动会产生亚像素位移，边缘不再逐像素相同；仅此时改用相邻行平滑后的校验。
            foreach (var candidate in shortlist
                         .Select(c => (c.Shift, Score: OverlapDifferenceSmoothed(pixels, content, c.Shift, true)))
                         .GroupBy(c => Math.Sign(c.Shift))
                         .SelectMany(group => group.OrderBy(c => c.Score).Take(2)))
            {
                if (candidate.Score >= 8) continue;
                var score = OverlapDifferenceSmoothed(pixels, content, candidate.Shift, false);
                if (score < 8) verified.Add((candidate.Shift, score));
                if (score < 8 && score < verifiedScore)
                {
                    verifiedScore = score;
                    bestShift = candidate.Shift;
                }
            }
        }
        if (bestShift == 0)
            return Fail("未找到可校验的滚动内容重叠；请避开动画，上下滚动时保留至少约 4% 的上一屏内容");
        if (verified.Any(c => Math.Abs(c.Shift - bestShift) > 2 && Math.Abs(c.Score - verifiedScore) < 0.05))
            return Fail("画面存在重复纹理，多个滚动距离同样匹配，无法确定真实位移");

        var nextPosition = _position + bestShift;
        var extended = false;
        if (nextPosition > _maximumPosition)
        {
            var extension = (int)(nextPosition - _maximumPosition);
            var footer = frameHeight - bottom;
            // 只在底部超出历史边界时追加，折返到已抓取部分不重复入图。
            RemoveTail(_footerHeight ?? footer);
            var appendHeight = Math.Min(extension + footer, MaximumHeight - Height);
            _strips.Add(CopyStrip(frame, bottom - extension, appendHeight));
            Height += appendHeight;
            _footerHeight = footer;
            _maximumPosition = nextPosition;
            extended = true;
        }
        else if (nextPosition < _minimumPosition)
        {
            var extension = (int)(_minimumPosition - nextPosition);
            RemoveHead(_headerHeight ?? top);
            var prependHeight = Math.Min(extension + top, MaximumHeight - Height);
            // 达到上限时保留与已有图片相邻的部分，避免中间缺行。
            _strips.Insert(0, CopyStrip(frame, top + extension - prependHeight, prependHeight));
            Height += prependHeight;
            _headerHeight = top;
            _minimumPosition = nextPosition;
            extended = true;
        }
        _position = nextPosition;
        _lastFrame = frame;
        _lastPixels = pixels;
        return Height == MaximumHeight ? StitchResult.HeightLimit : extended ? StitchResult.Appended : StitchResult.Unchanged;
    }

    private StitchResult Fail(string reason)
    {
        FailureReason = reason;
        return StitchResult.Failed;
    }

    private Rect FindScrollingContent(byte[] pixels, int height)
    {
        // 连续的同位置静止行/列只用于去掉固定边缘；不从中间随意删除内容。
        bool StillRow(int y)
        {
            var changed = 0;
            var count = 0;
            for (var x = 0; x < Width; x += 3)
            {
                var p = (y * Width + x) * 4;
                if (PixelError(_lastPixels, p, pixels, p) > 18) changed++;
                count++;
            }
            return changed <= Math.Max(1, count / 200);
        }
        var top = 0;
        while (top < height / 3 && StillRow(top)) top++;
        var bottom = height;
        while (bottom > height * 2 / 3 && StillRow(bottom - 1)) bottom--;
        // 若直到上限都没遇到运动行，就不把它猜成固定页头/页脚。
        if (top == height / 3) top = 0;
        if (bottom == height * 2 / 3) bottom = height;
        bool HasTexture(int from, int to)
        {
            var edges = 0;
            for (var y = from; y < to; y++)
                for (var x = 1; x < Width; x += 3)
                {
                    var p = (y * Width + x) * 4;
                    if (PixelError(pixels, p, pixels, p - 4) > 24 && ++edges >= 12) return true;
                }
            return false;
        }
        // 两帧共同的空白不等于固定控件，不因此裁去正文的空白边缘。
        if (!HasTexture(0, top)) top = 0;
        if (!HasTexture(bottom, height)) bottom = height;
        bool StillColumn(int x)
        {
            var changed = 0;
            var count = 0;
            for (var y = top; y < bottom; y += 3)
            {
                var p = (y * Width + x) * 4;
                if (PixelError(_lastPixels, p, pixels, p) > 18) changed++;
                count++;
            }
            return changed <= Math.Max(1, count / 200);
        }
        var left = 0;
        while (left < Width / 4 && StillColumn(left)) left++;
        var right = Width;
        while (right > Width * 3 / 4 && StillColumn(right - 1)) right--;
        if (left == Width / 4) left = 0;
        if (right == Width * 3 / 4) right = Width;
        return new Rect(left, top, right - left, bottom - top);
    }

    private static int PixelError(byte[] a, int ai, byte[] b, int bi) =>
        Math.Abs(a[ai] - b[bi]) + Math.Abs(a[ai + 1] - b[bi + 1]) + Math.Abs(a[ai + 2] - b[bi + 2]);

    private double OverlapDifference(byte[] pixels, Rect content, int shift, bool sampled)
    {
        long difference = 0;
        long texturedDifference = 0;
        long texturedCount = 0;
        long matchedTextureCount = 0;
        long count = 0;
        var stride = Width * 4;
        var startY = (int)content.Top + Math.Max(0, -shift);
        var endY = (int)content.Bottom - Math.Max(0, shift);
        for (var y = startY; y < endY; y += sampled ? Math.Max(1, (endY - startY) / 160) : 1)
            for (var x = (int)content.Left; x < content.Right; x += sampled ? Math.Max(1, (int)content.Width / 160) : 1)
            {
                var oldIndex = (y + shift) * stride + x * 4;
                var newIndex = y * stride + x * 4;
                var error = PixelError(_lastPixels, oldIndex, pixels, newIndex);
                difference += error;
                // 用全部颜色通道寻找纹理，不能只依赖蓝色通道。
                var edge = y > startY &&
                    (PixelError(pixels, newIndex, pixels, newIndex - stride) > 24 ||
                     PixelError(_lastPixels, oldIndex, _lastPixels, oldIndex - stride) > 24);
                if (edge)
                {
                    // 少量动态图片/光标不应否定整块正文，但多数纹理必须对应。
                    texturedDifference += Math.Min(error, 90);
                    if (error <= 45) matchedTextureCount++;
                    texturedCount++;
                }
                count++;
            }
        if (count == 0 || texturedCount < Math.Max(4, count / 1000)) return double.MaxValue;
        var textureError = texturedDifference / (texturedCount * 3.0);
        if (matchedTextureCount / (double)texturedCount < 0.8 || textureError >= 16) return double.MaxValue;
        return Math.Max(difference / (count * 3.0), textureError / 2);
    }

    private double OverlapDifferenceSmoothed(byte[] pixels, Rect content, int shift, bool sampled)
    {
        long difference = 0;
        long texturedDifference = 0;
        long texturedCount = 0;
        long matchedTextureCount = 0;
        long count = 0;
        var stride = Width * 4;
        var startY = (int)content.Top + Math.Max(0, -shift);
        var endY = (int)content.Bottom - Math.Max(0, shift);
        var top = (int)content.Top;
        var bottom = (int)content.Bottom;
        for (var y = startY; y < endY; y += sampled ? Math.Max(1, (endY - startY) / 160) : 1)
            for (var x = (int)content.Left; x < content.Right; x += sampled ? Math.Max(1, (int)content.Width / 160) : 1)
            {
                var oldY = y + shift;
                var oldIndex = oldY * stride + x * 4;
                var newIndex = y * stride + x * 4;
                var oldRed = 0; var oldGreen = 0; var oldBlue = 0;
                var newRed = 0; var newGreen = 0; var newBlue = 0;
                var oldCount = 0; var newCount = 0;
                var from = Math.Max(top, oldY - 1);
                var to = Math.Min(bottom - 1, oldY + 1);
                for (var row = from; row <= to; row++)
                {
                    var index = row * stride + x * 4;
                    oldBlue += _lastPixels[index];
                    oldGreen += _lastPixels[index + 1];
                    oldRed += _lastPixels[index + 2];
                    oldCount++;
                }
                from = Math.Max(top, y - 1);
                to = Math.Min(bottom - 1, y + 1);
                for (var row = from; row <= to; row++)
                {
                    var index = row * stride + x * 4;
                    newBlue += pixels[index];
                    newGreen += pixels[index + 1];
                    newRed += pixels[index + 2];
                    newCount++;
                }
                var error = Math.Abs(oldBlue / oldCount - newBlue / newCount) +
                    Math.Abs(oldGreen / oldCount - newGreen / newCount) +
                    Math.Abs(oldRed / oldCount - newRed / newCount);
                difference += error;
                var edge = y > startY &&
                    (PixelError(pixels, newIndex, pixels, newIndex - stride) > 24 ||
                     PixelError(_lastPixels, oldIndex, _lastPixels, oldIndex - stride) > 24);
                if (edge)
                {
                    texturedDifference += Math.Min(error, 90);
                    if (error <= 45) matchedTextureCount++;
                    texturedCount++;
                }
                count++;
            }
        if (count == 0 || texturedCount < Math.Max(4, count / 1000)) return double.MaxValue;
        var meanDifference = difference / (count * 3.0);
        var textureError = texturedDifference / (texturedCount * 3.0);
        var textureRatio = matchedTextureCount / (double)texturedCount;
        if (meanDifference >= 6 || textureRatio < 0.35 || textureError >= 20) return double.MaxValue;
        return meanDifference;
    }

    private void RemoveTail(int height)
    {
        while (height > 0 && _strips.Count > 0)
        {
            var strip = _strips[^1];
            var remove = Math.Min(height, strip.PixelHeight);
            _strips.RemoveAt(_strips.Count - 1);
            if (remove < strip.PixelHeight) _strips.Add(CopyStrip(strip, 0, strip.PixelHeight - remove));
            Height -= remove;
            height -= remove;
        }
    }

    private void RemoveHead(int height)
    {
        while (height > 0 && _strips.Count > 0)
        {
            var strip = _strips[0];
            var remove = Math.Min(height, strip.PixelHeight);
            _strips.RemoveAt(0);
            if (remove < strip.PixelHeight)
                _strips.Insert(0, CopyStrip(strip, remove, strip.PixelHeight - remove));
            Height -= remove;
            height -= remove;
        }
    }

    private BitmapSource CopyStrip(BitmapSource frame, int top, int height)
    {
        // 只保留新增长条，避免 CroppedBitmap 持有每次抓取的整帧。
        var stride = checked(Width * 4);
        var pixels = new byte[checked(stride * height)];
        frame.CopyPixels(new Int32Rect(0, top, Width, height), pixels, stride, 0);
        var strip = BitmapSource.Create(Width, height, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
        strip.Freeze();
        return strip;
    }

    private static byte[] RowSamples(byte[] pixels, int width, int height, int left, int right)
    {
        const int samples = 48;
        var result = new byte[height * samples];
        for (var y = 0; y < height; y++)
            for (var i = 0; i < samples; i++)
            {
                var start = left + i * (right - left) / samples;
                var end = Math.Min(right, Math.Max(start + 1, left + (i + 1) * (right - left) / samples));
                var total = 0;
                for (var x = start; x < end; x++)
                {
                    var p = (y * width + x) * 4;
                    total += (pixels[p] + pixels[p + 1] * 2 + pixels[p + 2]) / 4;
                }
                result[y * samples + i] = (byte)(total / (end - start));
            }
        return result;
    }

    public BitmapSource Compose()
    {
        var image = new WriteableBitmap(Width, Height, 96, 96, PixelFormats.Bgra32, null);
        var y = 0;
        foreach (var strip in _strips)
        {
            image.WritePixels(new Int32Rect(0, y, strip.PixelWidth, strip.PixelHeight), Pixels(strip), Width * 4, 0);
            y += strip.PixelHeight;
        }
        image.Freeze();
        return image;
    }
}
