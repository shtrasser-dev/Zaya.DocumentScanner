using System.Diagnostics.CodeAnalysis;

namespace Zaya.DocumentScanner.Impl.Shared.Services;

/// <summary>Reads and writes JSON files with shared serializer options.</summary>
public interface IJsonConfigurationService
{
    /// <summary>Deserializes <typeparamref name="T"/> from <paramref name="path"/>.</summary>
    T Read<T>(string path);

    T Read<T>(Stream stream);

    bool TryRead<T>(string path, [NotNullWhen(true)] out T? value);

    /// <summary>Serializes <paramref name="value"/> and writes atomically to <paramref name="path"/>.</summary>
    void Write<T>(string path, T value);
}
