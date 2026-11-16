using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Media;
using EasyShot.Core;

namespace EasyShot.Capture;

internal static class ScreenCapture
{
    public static bool IsNearlyBlack(BitmapSource image)
    {
        // 稀疏网格覆盖整张图，避免只检查中心点或把透明度误当作亮度。
        BitmapSource bitmap = image.Format == PixelFormats.Bgra32 ? image
            : new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
        var row = new byte[checked(bitmap.PixelWidth * 4)];
        var samples = 0;
        var nonBlack = 0;
        var stepX = Math.Max(1, bitmap.PixelWidth / 192);
        var stepY = Math.Max(1, bitmap.PixelHeight / 192);
        for (var y = 0; y < bitmap.PixelHeight; y += stepY)
        {
            bitmap.CopyPixels(new Int32Rect(0, y, bitmap.PixelWidth, 1), row, row.Length, 0);
            for (var x = 0; x < bitmap.PixelWidth; x += stepX)
            {
                var p = x * 4;
                if (row[p] > 8 || row[p + 1] > 8 || row[p + 2] > 8) nonBlack++;
                samples++;
            }
        }
        return samples > 0 && nonBlack / (double)samples < 0.005;
    }

    public static BitmapSource? CaptureWindow(CaptureWindowInfo window)
    {
        var screenDc = NativeMethods.GetDC(IntPtr.Zero);
        if (screenDc == IntPtr.Zero) return null;
        var memoryDc = NativeMethods.CreateCompatibleDC(screenDc);
        var bitmap = NativeMethods.CreateCompatibleBitmap(screenDc, window.Bounds.Width, window.Bounds.Height);
        var previous = IntPtr.Zero;
        try
        {
            if (memoryDc == IntPtr.Zero || bitmap == IntPtr.Zero) return null;
            previous = NativeMethods.SelectObject(memoryDc, bitmap);
            if (!NativeMethods.PrintWindow(window.Handle, memoryDc, 2)) return null;
            var source = Imaging.CreateBitmapSourceFromHBitmap(bitmap, IntPtr.Zero, Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        finally
        {
            if (previous != IntPtr.Zero) NativeMethods.SelectObject(memoryDc, previous);
            if (bitmap != IntPtr.Zero) NativeMethods.DeleteObject(bitmap);
            if (memoryDc != IntPtr.Zero) NativeMethods.DeleteDC(memoryDc);
            NativeMethods.ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    public static (BitmapSource Image, Int32Rect VirtualScreen) CaptureVirtualScreen()
    {
        var virtualScreen = ScreenInfo.GetVirtualScreen();
        return (CaptureRegion(virtualScreen), virtualScreen);
    }

    public static BitmapSource CaptureRegion(Int32Rect region)
    {
        if (region.Width <= 0 || region.Height <= 0) throw new ArgumentException("捕捉范围不能为空。");

        var screenDc = NativeMethods.GetDC(IntPtr.Zero);
        if (screenDc == IntPtr.Zero)
        {
            throw new InvalidOperationException("无法获取屏幕设备上下文。");
        }

        var memoryDc = NativeMethods.CreateCompatibleDC(screenDc);
        var bitmap = NativeMethods.CreateCompatibleBitmap(screenDc, region.Width, region.Height);
        var previous = IntPtr.Zero;

        try
        {
            if (memoryDc == IntPtr.Zero || bitmap == IntPtr.Zero) throw new InvalidOperationException("无法分配截图内存。");
            previous = NativeMethods.SelectObject(memoryDc, bitmap);

            if (!NativeMethods.BitBlt(memoryDc, 0, 0, region.Width, region.Height,
                    screenDc, region.X, region.Y, NativeMethods.SrcCopy | NativeMethods.CaptureBlt))
            {
                throw new InvalidOperationException("截取屏幕失败。");
            }

            var source = Imaging.CreateBitmapSourceFromHBitmap(bitmap, IntPtr.Zero, Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();

            return source;
        }
        finally
        {
            if (previous != IntPtr.Zero) NativeMethods.SelectObject(memoryDc, previous);
            if (bitmap != IntPtr.Zero) NativeMethods.DeleteObject(bitmap);
            if (memoryDc != IntPtr.Zero) NativeMethods.DeleteDC(memoryDc);
            NativeMethods.ReleaseDC(IntPtr.Zero, screenDc);
        }
    }
}
