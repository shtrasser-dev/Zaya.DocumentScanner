using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Zaya.Logging.Models;
using Zaya.Primitives;
using ZPixelFormat = Zaya.Primitives.PixelFormat;

namespace Zaya.DocumentScanner.Impl.Shared.Services.Impl;

[Log(LogLevel.Debug, LogParameters = true)]
public sealed class RawImageFileLoader : IRawImageFileLoader
{
    public IRawImage Load(string path)
    {
        using var bitmap = new Bitmap(path);
        var rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
        var data = bitmap.LockBits(rect, ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        try
        {
            var bytes = new byte[data.Stride * data.Height];
            Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);
            return new RawImage(bytes, data.Width, data.Height, data.Stride, ZPixelFormat.Bgra32);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }
}
