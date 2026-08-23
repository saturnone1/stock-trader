namespace StockTrader.MlTrainingService

open System
open System.IO
open System.Text.Json
open StockTrader.ServiceContracts.MachineLearning

type ArtifactStore(directory: string, json: JsonSerializerOptions) =
    do Directory.CreateDirectory directory |> ignore

    member _.Publish(jobId: string, artifact: MlModelArtifactContract) =
        match MlTrainingContractPolicy.ArtifactError artifact with
        | null ->
            let kindDirectory = Path.Combine(directory, artifact.ModelKind)
            Directory.CreateDirectory kindDirectory |> ignore
            let modelPath = Path.Combine(kindDirectory, artifact.ArtifactId + ".zip")
            let manifestPath = Path.Combine(kindDirectory, artifact.ArtifactId + ".json")
            if not (File.Exists modelPath) then
                use stream = new FileStream(modelPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read)
                stream.Write artifact.ModelBytes
                stream.Flush true
            if not (File.Exists manifestPath) then
                let manifest = {| jobId = jobId; artifact = artifact.MetadataOnly() |}
                use stream = new FileStream(manifestPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read)
                JsonSerializer.Serialize(stream, manifest, json)
                stream.Flush true
        | error -> invalidArg "artifact" error

    member this.Publish(jobId: string, result: MlTrainingJobResult) =
        match Option.ofObj result.RegimeArtifact with
        | Some artifact -> this.Publish(jobId, artifact)
        | None -> ()
        match Option.ofObj result.SignalArtifact with
        | Some artifact -> this.Publish(jobId, artifact)
        | None -> ()
