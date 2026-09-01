namespace StockTrader.TradingCoreAcceptanceDriver

open System
open System.Collections.Generic
open System.IO
open System.Text.Json
open StockTrader.ServiceContracts.TradingCore

module ManifestDriver =
    let private required name =
        match Environment.GetEnvironmentVariable name with
        | null | "" -> failwith $"Missing required setting: {name}"
        | value -> value

    let private dictionary (prefix: string) =
        let values = SortedDictionary<string,string>(StringComparer.Ordinal)
        Environment.GetEnvironmentVariables() |> Seq.cast<System.Collections.DictionaryEntry>
        |> Seq.iter (fun entry ->
            let key = Convert.ToString entry.Key
            if key.StartsWith(prefix, StringComparison.Ordinal) then
                values[key.Substring(prefix.Length).ToLowerInvariant().Replace('_', '-')] <-
                    Convert.ToString entry.Value)
        values :> IReadOnlyDictionary<_,_>

    let run () =
        let root = required "STOCKTRADER_ACCEPTANCE_MANIFEST_DIR"
        let fragments = Path.Combine(root, "fragments")
        let output = Path.Combine(root, "acceptance-manifest.json")
        let json = JsonSerializerOptions(JsonSerializerDefaults.Web)
        json.WriteIndented <- true
        Directory.CreateDirectory(fragments) |> ignore
        let results =
            Directory.GetFiles(fragments, "*.scenario.json")
            |> Array.map (fun path ->
                let value = JsonSerializer.Deserialize<AcceptanceScenarioResult>(File.ReadAllText path, json)
                if isNull value then invalidOp "empty-acceptance-scenario-result"
                value)
            |> Array.sortBy _.ScenarioCode
        let stopReasons =
            results
            |> Array.choose (fun value -> if value.Passed || isNull value.StopReason then None else Some value.StopReason)
            |> Array.distinct
            |> Array.sort
        let started = if results.Length = 0 then DateTime.UtcNow else results |> Array.minBy _.StartedAtUtc |> _.StartedAtUtc
        let ended = if results.Length = 0 then DateTime.UtcNow else results |> Array.maxBy _.EndedAtUtc |> _.EndedAtUtc
        let runId = required "STOCKTRADER_ACCEPTANCE_RUN_ID"
        let commit = required "STOCKTRADER_REPOSITORY_COMMIT"
        let buildId = required "STOCKTRADER_BUILD_ID"
        let images = dictionary "STOCKTRADER_IMAGE_DIGEST_"
        let assemblies = dictionary "STOCKTRADER_SHARED_ASSEMBLY_HASH_"
        let create manifestId passed reasons = AcceptanceManifestV1(
            TradingCoreAcceptanceVersions.Current, manifestId, runId,
            "IsolatedAcceptance", commit, buildId, images, assemblies, results,
            started, ended, passed, reasons)
        let passed = results.Length = TradingCoreAcceptanceScenarioCatalog.Required.Count
                     && (results |> Array.forall (fun value -> value.Passed))
        let candidate = create "" passed stopReasons
        let manifest = create (TradingCoreAcceptanceIdentity.Manifest candidate) passed stopReasons
        match Option.ofObj (TradingCoreAcceptancePolicy.ManifestError manifest) with
        | Some error ->
            let reasons = Array.append stopReasons [| error |]
            let failedCandidate = create "" false reasons
            let failed = create (TradingCoreAcceptanceIdentity.Manifest failedCandidate) false reasons
            File.WriteAllText(output + ".failed", JsonSerializer.Serialize(failed, json))
            2
        | None ->
            let temporary = output + ".tmp"
            File.WriteAllText(temporary, JsonSerializer.Serialize(manifest, json))
            File.Move(temporary, output, true)
            0
