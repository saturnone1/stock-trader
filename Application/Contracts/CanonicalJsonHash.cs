using System.Security.Cryptography;
using System.Text.Json;

namespace StockTrader.Application.Contracts;

/// <summary>
/// Produces a stable content identity for service-boundary payloads. Object properties are
/// ordered ordinally while array order is preserved because strategy and bar order is semantic.
/// </summary>
public static class CanonicalJsonHash
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public static string Compute<T>(T value, params string[] excludedPropertyNames)
    {
        ArgumentNullException.ThrowIfNull(value);
        var excluded = excludedPropertyNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var element = JsonSerializer.SerializeToElement(value, SerializerOptions);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            WriteCanonical(writer, element, excluded);
        }

        return Convert.ToHexString(SHA256.HashData(stream.ToArray()));
    }

    private static void WriteCanonical(
        Utf8JsonWriter writer,
        JsonElement element,
        IReadOnlySet<string> excluded)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject()
                             .Where(item => !excluded.Contains(item.Name))
                             .OrderBy(item => item.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonical(writer, property.Value, excluded);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                    WriteCanonical(writer, item, excluded);
                writer.WriteEndArray();
                break;
            default:
                element.WriteTo(writer);
                break;
        }
    }
}
