using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Zaya.DocumentScanner.Impl.Shared.Converters;

namespace Zaya.DocumentScanner.Impl.Shared.Services.Impl;

public sealed class JsonConfigurationService : IJsonConfigurationService
{
    private static JsonSerializerOptions Options { get; } = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new SettingsJsonConverter() },
    };

    public T Read<T>(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException(null, path);

        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<T>(json, Options) ?? throw new InvalidDataException(path);
    }

    public T Read<T>(Stream stream)
        => JsonSerializer.Deserialize<T>(stream, Options) ?? throw new InvalidDataException();

    public bool TryRead<T>(string path, [NotNullWhen(true)] out T? value)
    {
        try
        {
            value = Read<T>(path)!;
            return value is not null;
        }
        catch
        {
            value = default;
            return false;
        }
    }

    public void Write<T>(string path, T value)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var tmp = path + ".tmp";
        var json = JsonSerializer.Serialize(value, Options);
        File.WriteAllText(tmp, json);
        File.Move(tmp, path, overwrite: true);
    }
}
