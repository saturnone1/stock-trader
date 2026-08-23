namespace StockTrader.MlTrainingService

open System
open System.Text.Json
open Microsoft.Data.Sqlite
open StockTrader.ServiceContracts.MachineLearning
open StockTrader.MlTrainingCompute

type JobStore(path: string, json: JsonSerializerOptions) =
    let connectionString = $"Data Source={path};Mode=ReadWriteCreate;Cache=Shared;Pooling=False"
    let connect () =
        let connection = new SqliteConnection(connectionString)
        connection.Open()
        connection
    let scalarInt64 (command: SqliteCommand) = Convert.ToInt64(command.ExecuteScalar())

    do
        match IO.Path.GetDirectoryName(IO.Path.GetFullPath path) with
        | null | "" -> ()
        | parent -> IO.Directory.CreateDirectory parent |> ignore
        use connection = connect ()
        use command = connection.CreateCommand()
        command.CommandText <- """
PRAGMA journal_mode=WAL;
CREATE TABLE IF NOT EXISTS jobs (
 job_id TEXT PRIMARY KEY, input_hash TEXT NOT NULL, status TEXT NOT NULL,
 request_json TEXT NOT NULL, result_json TEXT NULL, cancel_requested INTEGER NOT NULL DEFAULT 0,
 accepted_at TEXT NOT NULL, started_at TEXT NULL, completed_at TEXT NULL, error TEXT NULL);
CREATE TABLE IF NOT EXISTS state (key TEXT PRIMARY KEY, value INTEGER NOT NULL);
INSERT OR IGNORE INTO state(key,value) VALUES('publication_revision',0);
UPDATE jobs SET status='Pending', started_at=NULL WHERE status='Running';
"""
        command.ExecuteNonQuery() |> ignore

    member _.Accept(request: MlTrainingJobRequest) =
        match Option.ofObj (MlTrainingContractPolicy.CompatibilityError request) with
        | Some error -> invalidArg "request" error
        | None ->
            use connection = connect ()
            use existing = connection.CreateCommand()
            existing.CommandText <- "SELECT input_hash,status FROM jobs WHERE job_id=$id"
            existing.Parameters.AddWithValue("$id", request.JobId) |> ignore
            use reader = existing.ExecuteReader()
            if reader.Read() then
                if not (String.Equals(reader.GetString(0), request.InputHash, StringComparison.OrdinalIgnoreCase)) then
                    invalidOp "job-id-input-conflict"
                MlTrainingJobAccepted(request.JobId, request.InputHash, reader.GetString(1), true)
            else
                reader.Close()
                use insert = connection.CreateCommand()
                insert.CommandText <- "INSERT INTO jobs(job_id,input_hash,status,request_json,accepted_at) VALUES($id,$hash,'Pending',$request,$at)"
                insert.Parameters.AddWithValue("$id", request.JobId) |> ignore
                insert.Parameters.AddWithValue("$hash", request.InputHash) |> ignore
                insert.Parameters.AddWithValue("$request", JsonSerializer.Serialize(request, json)) |> ignore
                insert.Parameters.AddWithValue("$at", DateTime.UtcNow.ToString("O")) |> ignore
                insert.ExecuteNonQuery() |> ignore
                MlTrainingJobAccepted(request.JobId, request.InputHash, MlTrainingJobStatuses.Pending, false)

    member _.Claim() =
        use connection = connect ()
        use transaction = connection.BeginTransaction()
        use select = connection.CreateCommand()
        select.Transaction <- transaction
        select.CommandText <- "SELECT job_id,request_json FROM jobs WHERE status='Pending' AND cancel_requested=0 ORDER BY accepted_at LIMIT 1"
        use reader = select.ExecuteReader()
        if not (reader.Read()) then None
        else
            let id, payload = reader.GetString(0), reader.GetString(1)
            reader.Close()
            use update = connection.CreateCommand()
            update.Transaction <- transaction
            update.CommandText <- "UPDATE jobs SET status='Running',started_at=$at WHERE job_id=$id AND status='Pending'"
            update.Parameters.AddWithValue("$id", id) |> ignore
            update.Parameters.AddWithValue("$at", DateTime.UtcNow.ToString("O")) |> ignore
            if update.ExecuteNonQuery() <> 1 then None
            else
                transaction.Commit()
                match Option.ofObj (JsonSerializer.Deserialize<MlTrainingJobRequest>(payload, json)) with
                | Some request -> Some request
                | None -> raise (IO.InvalidDataException("empty-stored-training-request"))

    member _.Complete(request: MlTrainingJobRequest, compute: MlComputeResult, durationMs: int64) =
        use connection = connect ()
        use transaction = connection.BeginTransaction()
        use query = connection.CreateCommand()
        query.Transaction <- transaction
        query.CommandText <- "SELECT accepted_at,started_at,cancel_requested FROM jobs WHERE job_id=$id"
        query.Parameters.AddWithValue("$id", request.JobId) |> ignore
        use reader = query.ExecuteReader()
        reader.Read() |> ignore
        let acceptedAt = DateTime.Parse(reader.GetString(0), null, Globalization.DateTimeStyles.RoundtripKind)
        let startedAt = DateTime.Parse(reader.GetString(1), null, Globalization.DateTimeStyles.RoundtripKind)
        let cancelled = reader.GetInt64(2) <> 0L
        reader.Close()
        use revision = connection.CreateCommand()
        revision.Transaction <- transaction
        let publishes = not cancelled && (not (isNull compute.RegimeArtifact) || not (isNull compute.SignalArtifact))
        revision.CommandText <-
            if publishes then "UPDATE state SET value=value+1 WHERE key='publication_revision'; SELECT value FROM state WHERE key='publication_revision'"
            else "SELECT value FROM state WHERE key='publication_revision'"
        let nextRevision = scalarInt64 revision
        let completedAt = DateTime.UtcNow
        let status = if cancelled then MlTrainingJobStatuses.Cancelled else compute.Status
        let result = MlTrainingJobResult(
            MlTrainingContractVersions.Current, request.JobId, request.InputHash, status,
            (if cancelled then "cancelled-before-publication" else compute.Message), nextRevision,
            acceptedAt, Nullable startedAt, Nullable completedAt, durationMs,
            (if cancelled then null else compute.RegimeArtifact),
            (if cancelled then null else compute.SignalArtifact), false)
        use update = connection.CreateCommand()
        update.Transaction <- transaction
        update.CommandText <- "UPDATE jobs SET status=$status,result_json=$result,completed_at=$at WHERE job_id=$id"
        update.Parameters.AddWithValue("$id", request.JobId) |> ignore
        update.Parameters.AddWithValue("$status", status) |> ignore
        update.Parameters.AddWithValue("$result", JsonSerializer.Serialize(result, json)) |> ignore
        update.Parameters.AddWithValue("$at", completedAt.ToString("O")) |> ignore
        update.ExecuteNonQuery() |> ignore
        transaction.Commit()
        result

    member _.ShouldPublish(id: string) =
        use connection = connect ()
        use command = connection.CreateCommand()
        command.CommandText <- "SELECT cancel_requested FROM jobs WHERE job_id=$id AND status='Running'"
        command.Parameters.AddWithValue("$id", id) |> ignore
        match command.ExecuteScalar() with
        | null -> false
        | value -> Convert.ToInt64(value) = 0L

    member _.Fail(id: string, message: string) =
        use connection = connect ()
        use command = connection.CreateCommand()
        command.CommandText <- "UPDATE jobs SET status='Failed',error=$error,completed_at=$at WHERE job_id=$id"
        command.Parameters.AddWithValue("$id", id) |> ignore
        command.Parameters.AddWithValue("$error", message) |> ignore
        command.Parameters.AddWithValue("$at", DateTime.UtcNow.ToString("O")) |> ignore
        command.ExecuteNonQuery() |> ignore

    member _.Get(id: string) =
        use connection = connect ()
        use command = connection.CreateCommand()
        command.CommandText <- "SELECT result_json,status,input_hash,error FROM jobs WHERE job_id=$id"
        command.Parameters.AddWithValue("$id", id) |> ignore
        use reader = command.ExecuteReader()
        if not (reader.Read()) then None
        elif not (reader.IsDBNull 0) then Some(box (JsonSerializer.Deserialize<MlTrainingJobResult>(reader.GetString(0), json)))
        else Some(box {| jobId=id; status=reader.GetString(1); inputHash=reader.GetString(2); error=(if reader.IsDBNull 3 then null else reader.GetString(3)) |})

    member _.Cancel(id: string) =
        use connection = connect ()
        use command = connection.CreateCommand()
        command.CommandText <- "UPDATE jobs SET cancel_requested=1,status=CASE WHEN status='Pending' THEN 'Cancelled' ELSE status END WHERE job_id=$id AND status IN ('Pending','Running')"
        command.Parameters.AddWithValue("$id", id) |> ignore
        command.ExecuteNonQuery() = 1

    member _.Status() =
        use connection = connect ()
        use command = connection.CreateCommand()
        command.CommandText <- "SELECT (SELECT value FROM state WHERE key='publication_revision'),SUM(status='Pending'),SUM(status='Running'),SUM(status IN ('Completed','PartiallyCompleted','InsufficientData')),MAX(completed_at) FROM jobs"
        use reader = command.ExecuteReader()
        reader.Read() |> ignore
        let number index = if reader.IsDBNull index then 0 else reader.GetInt32 index
        MlTrainingServiceStatus(MlTrainingContractVersions.Current, true, true,
            reader.GetInt64(0), number 1, number 2, number 3,
            (if reader.IsDBNull 4 then Nullable() else Nullable(DateTime.Parse(reader.GetString 4))), null)

    member _.PublishedResults() =
        use connection = connect ()
        use command = connection.CreateCommand()
        command.CommandText <- "SELECT result_json FROM jobs WHERE result_json IS NOT NULL ORDER BY completed_at"
        use reader = command.ExecuteReader()
        let results = ResizeArray<MlTrainingJobResult>()
        while reader.Read() do
            match Option.ofObj (JsonSerializer.Deserialize<MlTrainingJobResult>(reader.GetString 0, json)) with
            | Some result when result.RegimeArtifact <> null || result.SignalArtifact <> null -> results.Add result
            | _ -> ()
        results.ToArray()
