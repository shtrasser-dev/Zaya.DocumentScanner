using Avalonia.Media.Imaging;
using Zaya.Primitives;

namespace Zaya.DocumentScanner.Impl.Shared.Services;

/// <summary>
/// Converts a loaded page's raw pixel buffer into a displayable Avalonia bitmap. A DI-registered,
/// logged wrapper around <c>RawImageBitmapConverter</c>'s static methods - being an actual service
/// (rather than a plain static utility) means a failure here goes through the same
/// <c>WrapLogging</c> proxy as every other step of the pipeline, instead of only being visible if
/// the caller's own try/catch happens to show it.
/// </summary>
public interface IRawImageBitmapService
{
    WriteableBitmap ToBitmap(IRawImage image);

    Bitmap ToScaledBitmap(IRawImage image, int maxDimension);
}
