namespace StockTrader.MlTrainingService

open System
open System.IO

type ServiceConfig =
    { DatabasePath: string
      ArtifactDirectory: string
      SharedSecret: string
      ServerCertificatePath: string
      ServerCertificateKeyPath: string
      ClientCaPath: string
      PollMilliseconds: int }

module Configuration =
    let private required name =
        match Environment.GetEnvironmentVariable name with
        | null | "" -> failwith $"Missing required setting: {name}"
        | value -> value

    let load () =
        let root =
            match Environment.GetEnvironmentVariable "STOCKTRADER_ML_TRAINING_DATA" with
            | null | "" -> "/data"
            | value -> value
        { DatabasePath = Path.Combine(root, "jobs.db")
          ArtifactDirectory = Path.Combine(root, "artifacts")
          SharedSecret = required "STOCKTRADER_ML_TRAINING_SECRET"
          ServerCertificatePath = required "STOCKTRADER_ML_TRAINING_SERVER_CERT_PATH"
          ServerCertificateKeyPath = required "STOCKTRADER_ML_TRAINING_SERVER_KEY_PATH"
          ClientCaPath = required "STOCKTRADER_ML_TRAINING_CLIENT_CA_PATH"
          PollMilliseconds =
            match Environment.GetEnvironmentVariable "STOCKTRADER_ML_TRAINING_POLL_MILLISECONDS" with
            | null -> 500
            | raw ->
                match Int32.TryParse raw with
                | true, value when value >= 100 -> value
                | _ -> 500 }
