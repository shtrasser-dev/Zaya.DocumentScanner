using Zaya.Primitives;

namespace Zaya.DocumentScanner.Impl.Shared.Services.Impl;

/// <summary>
/// Crops a horizontal strip out of any <see cref="IRawImage"/> by slicing its row-major pixel
/// buffer directly — works regardless of where the source image came from (file, capture, etc.).
/// </summary>
public static class RawImageCropper
{
    public static RawImage CropRows(IRawImage source, int y, int height)
    {
        var rowBytes = source.Stride;
        var slice = source.GetPixelData().Slice(y * rowBytes, height * rowBytes);
        return new RawImage(slice.ToArray(), source.Width, height, rowBytes, source.Format);
    }

    /// <summary>
    /// Same as <see cref="CropRows"/>, but <paramref name="y"/> and <paramref name="y"/> + <paramref name="height"/>
    /// may fall outside [0, source.Height) - rows beyond either edge come back blank white
    /// (0xFF in every byte, which is white in every <see cref="Zaya.Primitives.PixelFormat"/> this
    /// app uses). Lets the scan window start above row 0 and end below the last row, so a line
    /// sitting right at the top or bottom of the real page still gets window positions where it
    /// sits safely clear of both edges, same as any line in the middle of the page would.
    /// </summary>
    public static RawImage CropRowsPadded(IRawImage source, int y, int height)
    {
        var rowBytes = source.Stride;
        var buffer = new byte[height * rowBytes];
        Array.Fill(buffer, (byte)0xFF);

        var srcStartRow = Math.Max(0, y);
        var srcEndRow = Math.Min(source.Height, y + height);
        if (srcEndRow > srcStartRow)
        {
            var destStartRow = srcStartRow - y;
            var rowCount = srcEndRow - srcStartRow;
            source.GetPixelData()
                .Slice(srcStartRow * rowBytes, rowCount * rowBytes)
                .CopyTo(buffer.AsSpan(destStartRow * rowBytes, rowCount * rowBytes));
        }

        return new RawImage(buffer, source.Width, height, rowBytes, source.Format);
    }
}
