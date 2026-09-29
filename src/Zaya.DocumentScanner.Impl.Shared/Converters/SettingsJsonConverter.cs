using System.Text.Json;
using System.Text.Json.Serialization;

namespace Zaya.DocumentScanner.Impl.Shared.Converters;

/// <summary>
/// (De)serializes a settings values bag as plain CLR types (bool/int/double/string/nested
/// dictionaries), so a value read back from JSON round-trips as the same type
/// <see cref="Zaya.Primitives.Settings.SettingDescriptorList"/>'s typed getters expect —
/// <see cref="JsonSerializer"/>'s default <c>object</c> handling would otherwise hand back
/// <see cref="JsonElement"/> instances.
/// </summary>
public sealed class SettingsJsonConverter : JsonConverter<Dictionary<string, object>>
{
    public override Dictionary<string, object> Read(
        ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var result = new Dictionary<string, object>();
        if (reader.TokenType != JsonTokenType.StartObject)
            return result;

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
                break;

            if (reader.TokenType != JsonTokenType.PropertyName)
                continue;

            var key = reader.GetString()!;
            reader.Read();

            var val = ReadValue(ref reader);
            if (val is not null)
                result[key] = val;
        }

        return result;
    }

    private static object? ReadValue(ref Utf8JsonReader reader)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.True:
                return true;
            case JsonTokenType.False:
                return false;
            case JsonTokenType.Number:
                if (reader.TryGetInt32(out var intVal))
                    return intVal;
                return reader.GetDouble();
            case JsonTokenType.String:
                return reader.GetString() ?? string.Empty;
            case JsonTokenType.Null:
                return null;
            case JsonTokenType.StartObject:
                {
                    var obj = new Dictionary<string, object>();
                    while (reader.Read())
                    {
                        if (reader.TokenType == JsonTokenType.EndObject)
                            break;
                        if (reader.TokenType == JsonTokenType.PropertyName)
                        {
                            var propKey = reader.GetString()!;
                            reader.Read();
                            obj[propKey] = ReadValue(ref reader)!;
                        }
                    }
                    return obj;
                }
            case JsonTokenType.StartArray:
                {
                    var list = new List<object>();
                    while (reader.Read())
                    {
                        if (reader.TokenType == JsonTokenType.EndArray)
                            break;
                        list.Add(ReadValue(ref reader)!);
                    }
                    return list;
                }
            default:
                return JsonSerializer.Deserialize<object>(ref reader);
        }
    }

    public override void Write(
        Utf8JsonWriter writer, Dictionary<string, object> value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        foreach (var kv in value)
        {
            writer.WritePropertyName(kv.Key);
            WriteValue(writer, kv.Value, options);
        }
        writer.WriteEndObject();
    }

    private static void WriteValue(Utf8JsonWriter writer, object? value, JsonSerializerOptions options)
    {
        switch (value)
        {
            case null:
                writer.WriteNullValue();
                break;
            case bool b:
                writer.WriteBooleanValue(b);
                break;
            case int i:
                writer.WriteNumberValue(i);
                break;
            case long l:
                writer.WriteNumberValue(l);
                break;
            case double d:
                writer.WriteNumberValue(d);
                break;
            case float f:
                writer.WriteNumberValue(f);
                break;
            case string s:
                writer.WriteStringValue(s);
                break;
            case Dictionary<string, object> dict:
                writer.WriteStartObject();
                foreach (var kv in dict)
                {
                    writer.WritePropertyName(kv.Key);
                    WriteValue(writer, kv.Value, options);
                }
                writer.WriteEndObject();
                break;
            case IReadOnlyDictionary<string, object> irdict:
                writer.WriteStartObject();
                foreach (var kv in irdict)
                {
                    writer.WritePropertyName(kv.Key);
                    WriteValue(writer, kv.Value, options);
                }
                writer.WriteEndObject();
                break;
            case System.Collections.IList list:
                writer.WriteStartArray();
                foreach (var item in list)
                    WriteValue(writer, item, options);
                writer.WriteEndArray();
                break;
            default:
                JsonSerializer.Serialize(writer, value, options);
                break;
        }
    }

    public override bool CanConvert(Type typeToConvert) => typeToConvert == typeof(Dictionary<string, object>);
}
