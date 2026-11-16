using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media.Imaging;
using EasyShot.Core;

namespace EasyShot.Capture;

internal static class ScreenshotOutput
{
    public static void Copy(BitmapSource image)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var png = new MemoryStream();
        encoder.Save(png);
        png.Position = 0;
        var data = new DataObject();
        data.SetImage(image);
        data.SetData("PNG", png);
        // 单次写入，不引入重试；持久化后允许释放编辑窗口和源图片。
        Marshal.ThrowExceptionForHR(NativeMethods.OleSetClipboard((System.Runtime.InteropServices.ComTypes.IDataObject)data));
        Marshal.ThrowExceptionForHR(NativeMethods.OleFlushClipboard());
    }
}
