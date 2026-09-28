using Zaya.Primitives;

namespace Zaya.DocumentScanner.Impl.Shared.Services;

/// <summary>Loads an image file (photo or scan) into an <see cref="IRawImage"/>.</summary>
public interface IRawImageFileLoader
{
    IRawImage Load(string path);
}
