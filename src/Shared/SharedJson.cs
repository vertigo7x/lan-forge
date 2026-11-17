using System.Text.Json;
using System.Text.Json.Serialization;

namespace LanForge.Shared;

/// <summary>
/// Opciones JSON compartidas. Única fuente de verdad: los bytes canónicos que se
/// firman y el formato alambre deben ser idénticos para que la verificación sea estable.
/// </summary>
public static class SharedJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static byte[] SerializeCanonical<T>(T value) => JsonSerializer.SerializeToUtf8Bytes(value, Options);

    public static T? Deserialize<T>(ReadOnlySpan<byte> utf8Json) => JsonSerializer.Deserialize<T>(utf8Json, Options);

    public static T? Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, Options);
}
