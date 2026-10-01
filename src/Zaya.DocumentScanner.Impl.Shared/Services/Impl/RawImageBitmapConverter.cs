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

    /// <summary>
    /// Same as <see cref="ToBitmap"/>, downscaled so its longer side is at most <paramref name="maxDimension"/> -
    /// for the file table's row thumbnail/hover preview, where the full source resolution (a multi-megapixel
    /// photo) would be wasted memory for a handful of on-screen pixels.
    /// </summary>
    /// <remarks>
    /// Samples the source pixels directly (nearest-neighbor) instead of building the full-size
    /// bitmap first and calling <c>Bitmap.CreateScaledBitmap</c> on it - Avalonia's Skia backend
    /// rejects resizing a <see cref="WriteableBitmap"/> ("Invalid source bitmap type"), since that
    /// resize path only accepts its own decoded/immutable bitmap implementations.
    /// </remarks>
    public static WriteableBitmap ToScaledBitmap(IRawImage image, int maxDimension)
    {
        if (image.Format != ZayaPixelFormat.Bgra32)
            throw new NotSupportedException($"Unsupported pixel format for preview: {image.Format}");

        var srcW = image.Width;
        var srcH = image.Height;
        var scale = Math.Min(1.0, maxDimension / (double)Math.Max(srcW, srcH));
        var dstW = Math.Max(1, (int)Math.Round(srcW * scale));
        var dstH = Math.Max(1, (int)Math.Round(srcH * scale));

        var bmp = new WriteableBitmap(new PixelSize(dstW, dstH), new Vector(96, 96), PixelFormats.Bgra8888, AlphaFormat.Unpremul);

        using var fb = bmp.Lock();
        var src = image.ToByteArray();
        var srcStride = image.Stride;
        var dstStride = fb.RowBytes;
        var dstPtr = fb.Address;

        var dstRow = new byte[dstStride];
        for (var dy = 0; dy < dstH; dy++)
        {
            var sy = Math.Min(srcH - 1, (int)((dy + 0.5) / scale));
            var srcRowOffset = sy * srcStride;
            for (var dx = 0; dx < dstW; dx++)
            {
                var sx = Math.Min(srcW - 1, (int)((dx + 0.5) / scale));
                var srcOffset = srcRowOffset + sx * 4;
                var dstOffset = dx * 4;
                if (srcOffset + 4 > src.Length)
                    continue;
                dstRow[dstOffset] = src[srcOffset];
                dstRow[dstOffset + 1] = src[srcOffset + 1];
                dstRow[dstOffset + 2] = src[srcOffset + 2];
                dstRow[dstOffset + 3] = src[srcOffset + 3];
            }

            Marshal.Copy(dstRow, 0, dstPtr + dy * dstStride, Math.Min(dstStride, dstW * 4));
        }

        return bmp;
    }
}
