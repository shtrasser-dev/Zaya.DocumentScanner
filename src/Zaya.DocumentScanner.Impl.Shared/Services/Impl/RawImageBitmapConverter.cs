using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Zaya.Primitives;
using ZayaPixelFormat = Zaya.Primitives.PixelFormat;

namespace Zaya.DocumentScanner.Impl.Shared.Services.Impl;

/// <summary>
/// Converts a loaded page's raw pixel buffer into a displayable Avalonia bitmap for the
/// "Исходный документ" preview. Only Bgra32 is handled — the only format
/// <see cref="RawImageFileLoader"/> ever produces.
/// </summary>
public static class RawImageBitmapConverter
{
    public static WriteableBitmap ToBitmap(IRawImage image)
    {
        if (image.Format != ZayaPixelFormat.Bgra32)
            throw new NotSupportedException($"Unsupported pixel format for preview: {image.Format}");

        var w = image.Width;
        var h = image.Height;
        var bmp = new WriteableBitmap(new PixelSize(w, h), new Vector(96, 96), PixelFormats.Bgra8888, AlphaFormat.Unpremul);

        using var fb = bmp.Lock();
        var bytes = image.ToByteArray();
        var srcStride = image.Stride;
        var dstStride = fb.RowBytes;
        var dstPtr = fb.Address;

        if (srcStride == dstStride)
        {
            Marshal.Copy(bytes, 0, dstPtr, Math.Min(bytes.Length, dstStride * h));
        }
        else
        {
            var rowBytes = Math.Min(w * 4, Math.Min(srcStride, dstStride));
            for (var y = 0; y < h; y++)
            {
                var srcOffset = y * srcStride;
                if (srcOffset + rowBytes > bytes.Length)
                    break;
                Marshal.Copy(bytes, srcOffset, dstPtr + y * dstStride, rowBytes);
            }
        }

        return bmp;
    }
}
