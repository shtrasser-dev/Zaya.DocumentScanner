using Zaya.Primitives;

namespace Zaya.DocumentScanner.Impl.Shared.Services.Impl;

/// <summary>Plain in-memory <see cref="IRawImage"/> over an owned byte buffer.</summary>
public sealed class RawImage : IRawImage
{
    private readonly byte[] _data;

    public RawImage(byte[] data, int width, int height, int stride, PixelFormat format)
    {
        _data = data;
        Width = width;
        Height = height;
        Stride = stride;
        Format = format;
    }

    public int Width { get; }
    public int Height { get; }
    public int Stride { get; }
    public PixelFormat Format { get; }
    public ReadOnlySpan<byte> GetPixelData() => _data;
    public byte[] ToByteArray() => (byte[])_data.Clone();
    public void Dispose()
    {
    }
}
