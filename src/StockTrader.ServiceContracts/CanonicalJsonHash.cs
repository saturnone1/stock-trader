using System.Security.Cryptography;
using System.Text.Json;

namespace StockTrader.ServiceContracts;

public static class CanonicalJsonHash
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static string Compute<T>(T value, params string[] excludedPropertyNames)
    {
        ArgumentNullException.ThrowIfNull(value);
        var excluded = excludedPropertyNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var element = JsonSerializer.SerializeToElement(value, Options);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
            Write(writer, element, excluded);
        return Convert.ToHexString(SHA256.HashData(stream.ToArray()));
    }

    private static void Write(Utf8JsonWriter writer, JsonElement value, IReadOnlySet<string> excluded)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            writer.WriteStartObject();
            foreach (var property in value.EnumerateObject()
                         .Where(item => !excluded.Contains(item.Name))
                         .OrderBy(item => item.Name, StringComparer.Ordinal))
            {
                writer.WritePropertyName(property.Name);
                Write(writer, property.Value, excluded);
            }
            writer.WriteEndObject();
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            writer.WriteStartArray();
            foreach (var item in value.EnumerateArray()) Write(writer, item, excluded);
            writer.WriteEndArray();
        }
        else value.WriteTo(writer);
    }
}
