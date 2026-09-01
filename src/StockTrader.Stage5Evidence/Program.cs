using System.Text.Json;
using StockTrader.ServiceContracts.TradingCore;

var json = new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true };
if (args.Length == 3 && args[0] == "candidate")
{
    var input = Read<Stage5CandidateManifestInput>(args[1]);
    var manifest = Stage5EvidencePolicy.SealCandidate(input);
    if (Stage5EvidencePolicy.CandidateError(manifest) is { } error)
        throw new InvalidDataException(error);
    Write(args[2], manifest);
    Console.WriteLine(manifest.CandidateId);
    return;
}

if (args.Length == 3 && args[0] == "local")
{
    var input = Read<Stage5LocalVerificationInput>(args[1]);
    var manifest = Stage5EvidencePolicy.SealLocal(input);
    if (Stage5EvidencePolicy.LocalError(manifest) is { } error)
        throw new InvalidDataException(error);
    Write(args[2], manifest);
    Console.WriteLine($"{manifest.ManifestId} passed={manifest.Passed}");
    Environment.ExitCode = manifest.Passed ? 0 : 2;
    return;
}

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

if (args.Length == 11 && args[0] == "index")
{
    var candidate = Read<TradingCoreCandidateManifestV1>(args[1]);
    var local = Read<LocalVerificationManifestV1>(args[2]);
    var reviewIdentity = args[3];
    var isolatedPath = args[4];
    var shadowPath = args[5];
    var cutoverPath = args[6];
    var recoveryPath = args[7];
    var rollbackPath = args[8];
    var finalRemotePath = args[9];
    var outputPath = args[10];
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
        candidate, local, isolated, operational, reviewIdentity, DateTime.UtcNow);
    if (Stage5EvidencePolicy.IndexError(index) is { } error)
        throw new InvalidDataException(error);
    Write(outputPath, index);
    Console.WriteLine($"{index.IndexId} accepted={index.Accepted}");
    Environment.ExitCode = index.Accepted ? 0 : 2;
    return;
}

throw new ArgumentException(
    "Usage: candidate INPUT OUTPUT | local INPUT OUTPUT | seal INPUT OUTPUT | " +
    "index CANDIDATE LOCAL REVIEW ISOLATED SHADOW CUTOVER RECOVERY ROLLBACK FINAL_REMOTE OUTPUT");

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
