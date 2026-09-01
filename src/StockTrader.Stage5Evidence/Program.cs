using System.Text.Json;
using StockTrader.ServiceContracts.TradingCore;

var json = new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true };
if (args.Length == 3 && args[0] == "seal")
{
    var inputPath = args[1];
    var outputPath = args[2];
    var input = Read<Stage5OperationalEvidenceInput>(inputPath);
    var manifest = Stage5EvidencePolicy.Seal(input);
    if (Stage5EvidencePolicy.Error(manifest) is { } error)
        throw new InvalidDataException(error);
    Write(outputPath, manifest);
    Console.WriteLine($"{manifest.EnvironmentClass} {manifest.ManifestId}");
    return;
}

if (args.Length == 10 && args[0] == "index")
{
    var candidateId = args[1];
    var reviewIdentity = args[2];
    var isolatedPath = args[3];
    var shadowPath = args[4];
    var cutoverPath = args[5];
    var recoveryPath = args[6];
    var rollbackPath = args[7];
    var finalRemotePath = args[8];
    var outputPath = args[9];
    var isolated = Read<AcceptanceManifestV1>(isolatedPath);
    var operational = new[]
    {
        Read<Stage5OperationalManifestV1>(shadowPath),
        Read<Stage5OperationalManifestV1>(cutoverPath),
        Read<Stage5OperationalManifestV1>(recoveryPath),
        Read<Stage5OperationalManifestV1>(rollbackPath),
        Read<Stage5OperationalManifestV1>(finalRemotePath),
    };
    var index = Stage5EvidencePolicy.DeriveIndex(
        candidateId, isolated, operational, reviewIdentity, DateTime.UtcNow);
    if (Stage5EvidencePolicy.IndexError(index) is { } error)
        throw new InvalidDataException(error);
    Write(outputPath, index);
    Console.WriteLine($"{index.IndexId} accepted={index.Accepted}");
    Environment.ExitCode = index.Accepted ? 0 : 2;
    return;
}

throw new ArgumentException(
    "Usage: seal INPUT OUTPUT | index CANDIDATE REVIEW ISOLATED SHADOW CUTOVER RECOVERY ROLLBACK FINAL_REMOTE OUTPUT");

T Read<T>(string path) => JsonSerializer.Deserialize<T>(File.ReadAllText(path), json)
    ?? throw new InvalidDataException($"Empty evidence document: {Path.GetFileName(path)}");

void Write<T>(string path, T value)
{
    var full = Path.GetFullPath(path);
    Directory.CreateDirectory(Path.GetDirectoryName(full)!);
    var temporary = full + ".tmp";
    File.WriteAllText(temporary, JsonSerializer.Serialize(value, json));
    File.Move(temporary, full, true);
}
