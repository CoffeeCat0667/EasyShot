using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Media;
using RapidOcrNet;
using SkiaSharp;

namespace EasyShot.Ocr;

internal sealed class OcrEngine : IDisposable
{
    internal static OcrEngine Shared { get; } = new();
    private RapidOcr? _engine;
    private readonly object _recognitionLock = new();
    private Task? _initialization;
    private bool _disposed;

    internal Task StartLoading()
    {
        lock (_recognitionLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            // 启动预加载和提前触发的识别始终等待同一个任务，失败不自动重试。
            return _initialization ??= Task.Run(() => Initialize(CancellationToken.None));
        }
    }

    private void Initialize(CancellationToken token)
    {
        if (_engine is not null) return;
        token.ThrowIfCancellationRequested();
        var directory = Path.Combine(Path.GetTempPath(), "EasyShot", "ocr-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        RapidOcr? engine = null;
        try
        {
            foreach (var name in new[] { "ppocr_v4_det.onnx", "ppocr_v4_cls.onnx", "ppocr_v4_rec.onnx", "ppocr_keys_v1.txt" })
            {
                token.ThrowIfCancellationRequested();
                using var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("EasyShot.Assets.models." + name)
                    ?? throw new InvalidOperationException($"缺少内嵌 OCR 文件：{name}");
                using var file = File.Create(Path.Combine(directory, name));
                resource.CopyTo(file);
            }
            engine = new RapidOcr();
            engine.InitModels(Path.Combine(directory, "ppocr_v4_det.onnx"),
                Path.Combine(directory, "ppocr_v4_cls.onnx"), Path.Combine(directory, "ppocr_v4_rec.onnx"),
                Path.Combine(directory, "ppocr_keys_v1.txt"));
            token.ThrowIfCancellationRequested();
            _engine = engine;
            engine = null;
        }
        finally
        {
            engine?.Dispose();
            // 仅删除本次创建的临时模型目录，不触碰用户文件。
            Directory.Delete(directory, true);
        }
    }

    public string Recognize(BitmapSource image, IProgress<string> progress, CancellationToken token)
    {
        var loading = StartLoading();
        if (!loading.IsCompleted) progress.Report("正在等待启动时的 OCR 模型加载完成…");
        loading.WaitAsync(token).GetAwaiter().GetResult();
        // 推理实例不能被并发使用；不同结果窗口复用模型而不重复初始化。
        lock (_recognitionLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            token.ThrowIfCancellationRequested();
            return RecognizeCore(image, progress, token);
        }
    }

    private string RecognizeCore(BitmapSource image, IProgress<string> progress, CancellationToken token)
    {
        const int blockHeight = 1600;
        const int overlap = 80;
        var lines = new List<(double Y, double X, string Text)>();
        var options = RapidOcrOptions.Default with { ImgResize = 1600, ClsPreserveAspectRatio = false };
        for (var top = 0; top < image.PixelHeight; top += blockHeight - overlap)
        {
            token.ThrowIfCancellationRequested();
            var height = Math.Min(blockHeight, image.PixelHeight - top);
            progress.Report($"正在识别第 {top + 1}–{top + height} 行像素，共 {image.PixelHeight} 行…");
            BitmapSource crop = new CroppedBitmap(image, new Int32Rect(0, top, image.PixelWidth, height));
            if (crop.Format != PixelFormats.Bgra32)
                crop = new FormatConvertedBitmap(crop, PixelFormats.Bgra32, null, 0);
            using var bitmap = new SKBitmap(new SKImageInfo(image.PixelWidth, height, SKColorType.Bgra8888, SKAlphaType.Unpremul));
            if (bitmap.GetPixels() == IntPtr.Zero) throw new InvalidOperationException("无法分配 OCR 图片内存。");
            crop.CopyPixels(Int32Rect.Empty, bitmap.GetPixels(), checked(bitmap.RowBytes * height), bitmap.RowBytes);
            var result = _engine!.Detect(bitmap, options, token);
            var minimumY = top == 0 ? 0 : top + overlap / 2;
            var maximumY = top + height == image.PixelHeight ? image.PixelHeight : top + height - overlap / 2;
            foreach (var block in result.TextBlocks)
            {
                if (string.IsNullOrWhiteSpace(block.Text) || block.BoxPoints.Length == 0) continue;
                var centerY = block.BoxPoints.Average(p => (double)p.Y) + top;
                // 重叠区按位置归属，保留正文中本来就重复出现的相同文字。
                if (centerY >= minimumY && centerY < maximumY)
                    lines.Add((centerY, block.BoxPoints.Min(p => (double)p.X), block.Text));
            }
            if (top + height == image.PixelHeight) break;
        }
        token.ThrowIfCancellationRequested();
        return string.Join(Environment.NewLine, lines.OrderBy(line => line.Y).ThenBy(line => line.X).Select(line => line.Text));
    }

    public void Dispose()
    {
        Task? loading;
        lock (_recognitionLock)
        {
            _disposed = true;
            loading = _initialization;
        }
        // 初始化只在工作线程执行，不依赖 UI 线程；结束后再释放原生会话。
        try { loading?.GetAwaiter().GetResult(); }
        catch { /* 加载错误由启动提示和 OCR 结果窗口报告。 */ }
        lock (_recognitionLock)
        {
            _engine?.Dispose();
            _engine = null;
        }
    }
}
