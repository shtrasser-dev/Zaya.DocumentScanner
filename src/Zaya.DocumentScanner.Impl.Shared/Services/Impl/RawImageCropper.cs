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
}
