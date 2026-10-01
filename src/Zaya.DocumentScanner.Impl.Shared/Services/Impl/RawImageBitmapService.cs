using Avalonia.Media.Imaging;
using Zaya.Logging.Models;
using Zaya.Primitives;

namespace Zaya.DocumentScanner.Impl.Shared.Services.Impl;

[Log(LogLevel.Debug)]
public sealed class RawImageBitmapService : IRawImageBitmapService
{
    public WriteableBitmap ToBitmap(IRawImage image) => RawImageBitmapConverter.ToBitmap(image);

    public Bitmap ToScaledBitmap(IRawImage image, int maxDimension) => RawImageBitmapConverter.ToScaledBitmap(image, maxDimension);
}
