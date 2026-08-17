using System.Text.Json;

namespace StockTrader.Application.Optimization;

/// <summary>
/// Persisted optimization request JSON boundary. Reads the former EF-entity <c>basePattern.id</c>
/// shape and rewrites all new jobs with the storage-independent strategy reference.
/// </summary>
public static class OptimizeRequestJsonCodec
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public static string Serialize(OptimizeRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return JsonSerializer.Serialize(request, Options);
    }

    public static OptimizeRequest? Deserialize(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            var request = JsonSerializer.Deserialize<OptimizeRequest>(json, Options);
            if (request?.BasePattern.StoredStrategyId is > 0) return request;

            using var parsed = JsonDocument.Parse(json);
            if (TryGetProperty(parsed.RootElement, "basePattern", out var basePattern)
                && TryGetProperty(basePattern, "id", out var legacyId)
                && legacyId.TryGetInt32(out var id)
                && id > 0
                && request is not null)
            {
                request.BasePattern.StoredStrategyId = id;
            }
            return request;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static OptimizeRequest Clone(OptimizeRequest request) =>
        Deserialize(Serialize(request)) ?? throw new InvalidOperationException(
            "최적화 요청을 복제하지 못했습니다.");

    private static bool TryGetProperty(JsonElement value, string name, out JsonElement property)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            foreach (var candidate in value.EnumerateObject())
            {
                if (string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    property = candidate.Value;
                    return true;
                }
            }
        }

        property = default;
        return false;
    }
}
