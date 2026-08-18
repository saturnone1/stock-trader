using System.Globalization;
using System.Text;
using System.Text.Json;
using StockTrader.Application.Research;

namespace StockTrader.Services.Financial;

public class FinancialSnapshotFileParser
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<List<FinancialSnapshotImportItem>> ParseFileAsync(string path, CancellationToken ct)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        return extension switch
        {
            ".json" => await ParseJsonAsync(path, ct),
            ".csv" => await ParseCsvAsync(path, ct),
            _ => throw new InvalidOperationException($"Unsupported financial import file type: {extension}")
        };
    }

    private static async Task<List<FinancialSnapshotImportItem>> ParseJsonAsync(string path, CancellationToken ct)
    {
        var text = await File.ReadAllTextAsync(path, ct);
        if (string.IsNullOrWhiteSpace(text))
            return [];

        var trimmed = text.TrimStart();
        if (trimmed.StartsWith("["))
            return JsonSerializer.Deserialize<List<FinancialSnapshotImportItem>>(text, JsonOptions) ?? [];

        var single = JsonSerializer.Deserialize<FinancialSnapshotImportItem>(text, JsonOptions);
        return single is null ? [] : [single];
    }

    private static async Task<List<FinancialSnapshotImportItem>> ParseCsvAsync(string path, CancellationToken ct)
    {
        var lines = await File.ReadAllLinesAsync(path, ct);
        if (lines.Length <= 1)
            return [];

        var headers = SplitCsvLine(lines[0]).Select(NormalizeHeader).ToList();
        var result = new List<FinancialSnapshotImportItem>();

        for (var i = 1; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i]))
                continue;

            var values = SplitCsvLine(lines[i]);
            string? Get(string key)
            {
                var index = headers.FindIndex(header => header == NormalizeHeader(key));
                return index >= 0 && index < values.Count ? values[index] : null;
            }

            result.Add(new FinancialSnapshotImportItem
            {
                Symbol = Get("symbol"),
                AsOfDate = ParseDate(Get("asOfDate") ?? Get("date")),
                Source = Get("source"),
                PeRatio = ParseDecimal(Get("peRatio") ?? Get("per") ?? Get("pe")),
                PbRatio = ParseDecimal(Get("pbRatio") ?? Get("pbr") ?? Get("pb")),
                RoePercent = ParseDecimal(Get("roePercent") ?? Get("roe")),
                OperatingMarginPercent = ParseDecimal(Get("operatingMarginPercent") ?? Get("operatingMargin")),
                RevenueCurrent = ParseDecimal(Get("revenueCurrent")),
                RevenuePrevious = ParseDecimal(Get("revenuePrevious")),
                OperatingIncomeCurrent = ParseDecimal(Get("operatingIncomeCurrent")),
                OperatingIncomePrevious = ParseDecimal(Get("operatingIncomePrevious")),
                NetIncomeCurrent = ParseDecimal(Get("netIncomeCurrent")),
                NetIncomePrevious = ParseDecimal(Get("netIncomePrevious")),
                Notes = Get("notes")
            });
        }

        return result;
    }

    private static List<string> SplitCsvLine(string line)
    {
        var fields = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];
            if (ch == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (ch == ',' && !inQuotes)
            {
                fields.Add(current.ToString().Trim());
                current.Clear();
            }
            else
            {
                current.Append(ch);
            }
        }

        fields.Add(current.ToString().Trim());
        return fields;
    }

    private static string NormalizeHeader(string value) =>
        string.Concat(value.Where(ch => !char.IsWhiteSpace(ch) && ch != '_' && ch != '-')).ToLowerInvariant();

    private static decimal? ParseDecimal(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        return decimal.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    private static DateTime? ParseDate(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        return DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var value)
            ? value.Date
            : null;
    }
}
