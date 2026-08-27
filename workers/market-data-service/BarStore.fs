namespace StockTrader.MarketDataService

open System
open System.Collections.Generic
open System.Globalization
open System.Threading
open System.Threading.Tasks
open Microsoft.Data.Sqlite
open StockTrader.Domain.MarketData
open StockTrader.ServiceContracts.MarketData

exception private AlreadyApplied of MarketDataUpsertResponse

type BarStore(databasePath: string) =
    let gate = new SemaphoreSlim(1, 1)
    let invariant = CultureInfo.InvariantCulture
    // One service process owns this SQLite database. Disabling pooling prevents stale handles
    // from blocking backup/restore replacement after every command has been disposed.
    let connectionString =
        let builder = SqliteConnectionStringBuilder()
        builder.DataSource <- databasePath
        builder.Mode <- SqliteOpenMode.ReadWriteCreate
        builder.Pooling <- false
        builder.ToString()

    let command (connection: SqliteConnection) (transaction: SqliteTransaction option) sql =
        let value = connection.CreateCommand()
        value.CommandText <- sql
        transaction |> Option.iter (fun tx -> value.Transaction <- tx)
        value

    let parameter (cmd: SqliteCommand) name value =
        let boxed = box value
        cmd.Parameters.AddWithValue(name, if isNull boxed then box DBNull.Value else boxed) |> ignore

    let execute (cmd: SqliteCommand) ct = task {
        let! _ = cmd.ExecuteNonQueryAsync(ct)
        return ()
    }

    let scalarInt64 (cmd: SqliteCommand) ct = task {
        let! value = cmd.ExecuteScalarAsync(ct)
        return if isNull value || value = box DBNull.Value then 0L else Convert.ToInt64(value, invariant)
    }

    let readBar (reader: SqliteDataReader) =
        let vwap =
            if reader.IsDBNull(8) then Nullable<decimal>()
            else
                reader.GetString(8)
                |> Option.ofObj
                |> Option.map (fun value -> Nullable(Decimal.Parse(value, invariant)))
                |> Option.defaultValue (Nullable())
        MarketDataBar(
            reader.GetString(0), reader.GetString(1),
            DateTime.Parse(reader.GetString(2), invariant, DateTimeStyles.RoundtripKind),
            Decimal.Parse(reader.GetString(3), invariant), Decimal.Parse(reader.GetString(4), invariant),
            Decimal.Parse(reader.GetString(5), invariant), Decimal.Parse(reader.GetString(6), invariant),
            reader.GetInt64(7), vwap)

    member _.InitializeAsync(ct: CancellationToken) = task {
        let directory = IO.Path.GetDirectoryName(databasePath)
        if not (String.IsNullOrWhiteSpace(directory)) then IO.Directory.CreateDirectory(directory) |> ignore
        use connection = new SqliteConnection(connectionString)
        do! connection.OpenAsync(ct)
        use cmd = command connection None """
PRAGMA journal_mode=WAL;
PRAGMA synchronous=FULL;
PRAGMA foreign_keys=ON;
CREATE TABLE IF NOT EXISTS Metadata (Key TEXT PRIMARY KEY, Value TEXT NOT NULL);
INSERT OR IGNORE INTO Metadata(Key, Value) VALUES ('LatestRevision', '0');
CREATE TABLE IF NOT EXISTS Bars (
  Provider TEXT NOT NULL, Symbol TEXT NOT NULL, TimeFrame TEXT NOT NULL,
  AdjustmentMode TEXT NOT NULL, TimestampUtc TEXT NOT NULL,
  Open TEXT NOT NULL, High TEXT NOT NULL, Low TEXT NOT NULL, Close TEXT NOT NULL,
  Volume INTEGER NOT NULL, Vwap TEXT NULL, ContentHash TEXT NOT NULL, Revision INTEGER NOT NULL,
  PRIMARY KEY(Provider, Symbol, TimeFrame, AdjustmentMode, TimestampUtc));
CREATE INDEX IF NOT EXISTS IX_Bars_Range
  ON Bars(Provider, Symbol, TimeFrame, AdjustmentMode, TimestampUtc);
CREATE TABLE IF NOT EXISTS EvidenceRanges (
  Provider TEXT NOT NULL, Symbol TEXT NOT NULL, TimeFrame TEXT NOT NULL,
  AdjustmentMode TEXT NOT NULL, FromUtc TEXT NOT NULL, ToUtc TEXT NOT NULL,
  Market TEXT NOT NULL, CalendarVersion TEXT NOT NULL, IsComplete INTEGER NOT NULL,
  Revision INTEGER NOT NULL, PRIMARY KEY(Provider, Symbol, TimeFrame, AdjustmentMode, FromUtc, ToUtc));
CREATE TABLE IF NOT EXISTS Corrections (
  Revision INTEGER NOT NULL, Sequence INTEGER NOT NULL, Provider TEXT NOT NULL,
  Symbol TEXT NOT NULL, TimeFrame TEXT NOT NULL, AdjustmentMode TEXT NOT NULL,
  TimestampUtc TEXT NOT NULL, PreviousHash TEXT NOT NULL, CurrentHash TEXT NOT NULL,
  OccurredAtUtc TEXT NOT NULL, PRIMARY KEY(Revision, Sequence));
CREATE TABLE IF NOT EXISTS AppliedRequests (
  RequestId TEXT PRIMARY KEY, Inserted INTEGER NOT NULL, Unchanged INTEGER NOT NULL,
  Corrected INTEGER NOT NULL, Revision INTEGER NOT NULL, AppliedAtUtc TEXT NOT NULL);
"""
        do! execute cmd ct
    }

    member private _.UpsertCoreAsync(request: MarketDataUpsertRequest, ct: CancellationToken) = task {
        ContractPolicy.validateVersion request.ContractVersion
        if String.IsNullOrWhiteSpace(request.RequestId) || request.RequestId.Length > 128 then
            invalidArg "requestId" "RequestId must contain 1-128 characters"
        let provider = ContractPolicy.normalizeProvider request.Provider
        let normalized = request.Bars |> Seq.map ContractPolicy.normalizeBar |> Seq.toArray
        for bar in normalized do
            let frame = ContractPolicy.normalizeTimeFrame bar.TimeFrame
            ContractPolicy.normalizeAdjustment provider frame request.AdjustmentMode |> ignore
        if request.RequestedFromUtc.HasValue <> request.RequestedToUtc.HasValue then
            invalidArg "requestedRange" "Both requested range values must be present or absent"
        if request.RequestedFromUtc.HasValue then
            let fromUtc, toUtc = ContractPolicy.ensureRange request.RequestedFromUtc.Value request.RequestedToUtc.Value
            if normalized |> Array.exists (fun bar -> bar.TimestampUtc < fromUtc || bar.TimestampUtc > toUtc) then
                invalidArg "bars" "Every bar must be inside the declared requested range"

        do! gate.WaitAsync(ct)
        try
            use connection = new SqliteConnection(connectionString)
            do! connection.OpenAsync(ct)
            use tx = connection.BeginTransaction()
            use prior = command connection (Some tx) "SELECT Inserted, Unchanged, Corrected, Revision FROM AppliedRequests WHERE RequestId=@id"
            parameter prior "@id" request.RequestId
            use! priorReader = prior.ExecuteReaderAsync(ct)
            if priorReader.Read() then
                let response = MarketDataUpsertResponse(request.RequestId, priorReader.GetInt32(0), priorReader.GetInt32(1), priorReader.GetInt32(2), priorReader.GetInt64(3), true)
                priorReader.Close()
                tx.Rollback()
                raise (AlreadyApplied response)
            priorReader.Close()

            let mutable inserted, unchanged, corrected = 0, 0, 0
            let changes = ResizeArray<MarketDataBar * string * string>()
            for bar in normalized do
                use existing = command connection (Some tx) """
SELECT ContentHash FROM Bars WHERE Provider=@provider AND Symbol=@symbol AND TimeFrame=@frame
 AND AdjustmentMode=@adjustment AND TimestampUtc=@timestamp"""
                parameter existing "@provider" (provider.ToString())
                parameter existing "@symbol" bar.Symbol
                parameter existing "@frame" bar.TimeFrame
                parameter existing "@adjustment" request.AdjustmentMode
                parameter existing "@timestamp" (ContractPolicy.utc(bar.TimestampUtc).ToString("O", invariant))
                let! current = existing.ExecuteScalarAsync(ct)
                let nextHash = ContractPolicy.barHash bar
                if isNull current || current = box DBNull.Value then
                    inserted <- inserted + 1
                    changes.Add(bar, "", nextHash)
                elif string current = nextHash then unchanged <- unchanged + 1
                else
                    corrected <- corrected + 1
                    changes.Add(bar, string current, nextHash)

            use revisionCmd = command connection (Some tx) "SELECT CAST(Value AS INTEGER) FROM Metadata WHERE Key='LatestRevision'"
            let! currentRevision = scalarInt64 revisionCmd ct
            let revision = if changes.Count > 0 then currentRevision + 1L else currentRevision
            if changes.Count > 0 then
                use updateRevision = command connection (Some tx) "UPDATE Metadata SET Value=@value WHERE Key='LatestRevision'"
                parameter updateRevision "@value" (revision.ToString(invariant))
                do! execute updateRevision ct

            let mutable correctionSequence = 0
            for bar, previousHash, nextHash in changes do
                use upsert = command connection (Some tx) """
INSERT INTO Bars(Provider,Symbol,TimeFrame,AdjustmentMode,TimestampUtc,Open,High,Low,Close,Volume,Vwap,ContentHash,Revision)
VALUES(@provider,@symbol,@frame,@adjustment,@timestamp,@open,@high,@low,@close,@volume,@vwap,@hash,@revision)
ON CONFLICT(Provider,Symbol,TimeFrame,AdjustmentMode,TimestampUtc) DO UPDATE SET
 Open=excluded.Open, High=excluded.High, Low=excluded.Low, Close=excluded.Close,
 Volume=excluded.Volume, Vwap=excluded.Vwap, ContentHash=excluded.ContentHash, Revision=excluded.Revision"""
                parameter upsert "@provider" (provider.ToString())
                parameter upsert "@symbol" bar.Symbol
                parameter upsert "@frame" bar.TimeFrame
                parameter upsert "@adjustment" request.AdjustmentMode
                parameter upsert "@timestamp" (ContractPolicy.utc(bar.TimestampUtc).ToString("O", invariant))
                parameter upsert "@open" (bar.Open.ToString("G29", invariant))
                parameter upsert "@high" (bar.High.ToString("G29", invariant))
                parameter upsert "@low" (bar.Low.ToString("G29", invariant))
                parameter upsert "@close" (bar.Close.ToString("G29", invariant))
                parameter upsert "@volume" (box bar.Volume)
                parameter upsert "@vwap" (if bar.Vwap.HasValue then box (bar.Vwap.Value.ToString("G29", invariant)) else null)
                parameter upsert "@hash" nextHash
                parameter upsert "@revision" (box revision)
                do! execute upsert ct
                if previousHash <> "" then
                    correctionSequence <- correctionSequence + 1
                    use correction = command connection (Some tx) """
INSERT INTO Corrections VALUES(@revision,@sequence,@provider,@symbol,@frame,@adjustment,@timestamp,@previous,@current,@occurred)"""
                    parameter correction "@revision" (box revision)
                    parameter correction "@sequence" (box correctionSequence)
                    parameter correction "@provider" (provider.ToString())
                    parameter correction "@symbol" bar.Symbol
                    parameter correction "@frame" bar.TimeFrame
                    parameter correction "@adjustment" request.AdjustmentMode
                    parameter correction "@timestamp" (ContractPolicy.utc(bar.TimestampUtc).ToString("O", invariant))
                    parameter correction "@previous" previousHash
                    parameter correction "@current" nextHash
                    parameter correction "@occurred" (DateTime.UtcNow.ToString("O", invariant))
                    do! execute correction ct

            if request.RequestedFromUtc.HasValue then
                let fromUtc, toUtc = ContractPolicy.ensureRange request.RequestedFromUtc.Value request.RequestedToUtc.Value
                let symbolsAndFrames = normalized |> Seq.map (fun bar -> bar.Symbol, bar.TimeFrame) |> Seq.distinct
                for symbol, frame in symbolsAndFrames do
                    use range = command connection (Some tx) """
INSERT INTO EvidenceRanges VALUES(@provider,@symbol,@frame,@adjustment,@from,@to,@market,@calendar,@complete,@revision)
ON CONFLICT(Provider,Symbol,TimeFrame,AdjustmentMode,FromUtc,ToUtc) DO UPDATE SET
 Market=excluded.Market, CalendarVersion=excluded.CalendarVersion,
 IsComplete=excluded.IsComplete, Revision=excluded.Revision"""
                    parameter range "@provider" (provider.ToString())
                    parameter range "@symbol" symbol
                    parameter range "@frame" frame
                    parameter range "@adjustment" request.AdjustmentMode
                    parameter range "@from" (fromUtc.ToString("O", invariant))
                    parameter range "@to" (toUtc.ToString("O", invariant))
                    parameter range "@market" request.Market
                    parameter range "@calendar" request.CalendarVersion
                    parameter range "@complete" (box (if request.IsComplete then 1 else 0))
                    parameter range "@revision" (box revision)
                    do! execute range ct

            use applied = command connection (Some tx) "INSERT INTO AppliedRequests VALUES(@id,@inserted,@unchanged,@corrected,@revision,@applied)"
            parameter applied "@id" request.RequestId
            parameter applied "@inserted" (box inserted)
            parameter applied "@unchanged" (box unchanged)
            parameter applied "@corrected" (box corrected)
            parameter applied "@revision" (box revision)
            parameter applied "@applied" (DateTime.UtcNow.ToString("O", invariant))
            do! execute applied ct
            tx.Commit()
            return MarketDataUpsertResponse(request.RequestId, inserted, unchanged, corrected, revision, false)
        finally gate.Release() |> ignore
    }

    member this.UpsertAsync(request: MarketDataUpsertRequest, ct: CancellationToken) = task {
        try return! this.UpsertCoreAsync(request, ct)
        with AlreadyApplied response -> return response
    }

    member _.ReadRangeAsync(request: MarketDataRangeRequest, ct: CancellationToken) = task {
        ContractPolicy.validateVersion request.ContractVersion
        let provider = ContractPolicy.normalizeProvider request.Provider
        let frame = ContractPolicy.normalizeTimeFrame request.TimeFrame
        ContractPolicy.normalizeAdjustment provider frame request.AdjustmentMode |> ignore
        let symbol = ContractPolicy.normalizeSymbol request.Symbol
        let fromUtc, toUtc = ContractPolicy.ensureRange request.FromUtc request.ToUtc
        use connection = new SqliteConnection(connectionString)
        do! connection.OpenAsync(ct)
        use cmd = command connection None """
SELECT Symbol,TimeFrame,TimestampUtc,Open,High,Low,Close,Volume,Vwap FROM Bars
WHERE Provider=@provider AND Symbol=@symbol AND TimeFrame=@frame AND AdjustmentMode=@adjustment
 AND TimestampUtc>=@from AND TimestampUtc<=@to ORDER BY TimestampUtc"""
        parameter cmd "@provider" (provider.ToString())
        parameter cmd "@symbol" symbol
        parameter cmd "@frame" (frame.ToString())
        parameter cmd "@adjustment" request.AdjustmentMode
        parameter cmd "@from" (fromUtc.ToString("O", invariant))
        parameter cmd "@to" (toUtc.ToString("O", invariant))
        use! reader = cmd.ExecuteReaderAsync(ct)
        let bars = ResizeArray<MarketDataBar>()
        while reader.Read() do bars.Add(readBar reader)
        reader.Close()
        use revisionCmd = command connection None "SELECT CAST(Value AS INTEGER) FROM Metadata WHERE Key='LatestRevision'"
        let! revision = scalarInt64 revisionCmd ct
        use completeCmd = command connection None """
SELECT COUNT(*) FROM EvidenceRanges WHERE Provider=@provider AND Symbol=@symbol AND TimeFrame=@frame
 AND AdjustmentMode=@adjustment AND FromUtc<=@from AND ToUtc>=@to AND IsComplete=1"""
        parameter completeCmd "@provider" (provider.ToString())
        parameter completeCmd "@symbol" symbol
        parameter completeCmd "@frame" (frame.ToString())
        parameter completeCmd "@adjustment" request.AdjustmentMode
        parameter completeCmd "@from" (fromUtc.ToString("O", invariant))
        parameter completeCmd "@to" (toUtc.ToString("O", invariant))
        let! completeRanges = scalarInt64 completeCmd ct
        let hash = ContractPolicy.contentHash bars
        let evidence = MarketDataEvidenceContract(
            MarketDataContractVersions.Current,
            ContractPolicy.evidenceId (provider.ToString()) symbol (frame.ToString()) request.AdjustmentMode request.CalendarVersion revision hash,
            provider.ToString(), symbol, frame.ToString(), request.AdjustmentMode, request.Market,
            request.CalendarVersion, fromUtc, toUtc,
            (if bars.Count = 0 then Nullable() else Nullable(bars[0].TimestampUtc)),
            (if bars.Count = 0 then Nullable() else Nullable(bars[bars.Count - 1].TimestampUtc)),
            revision, completeRanges > 0L, hash)
        return MarketDataRangeResponse(evidence, bars.ToArray())
    }

    member this.ReadLatestAsync(request: MarketDataRangeRequest, ct: CancellationToken) = task {
        let! range = this.ReadRangeAsync(request, ct)
        return if range.Bars.Count = 0 then None else Some range.Bars[range.Bars.Count - 1]
    }

    member this.VerifyEvidenceAsync(request: MarketDataEvidenceVerificationRequest, ct: CancellationToken) = task {
        ContractPolicy.validateVersion request.ContractVersion
        let expected = request.Evidence
        ContractPolicy.validateVersion expected.ContractVersion
        let expectedIdentity =
            ContractPolicy.evidenceId expected.Provider expected.Symbol expected.TimeFrame
                expected.AdjustmentMode expected.CalendarVersion expected.Revision expected.ContentHash
        if not (String.Equals(expectedIdentity, expected.EvidenceId, StringComparison.Ordinal)) then
            invalidArg "evidence" "Market data evidence identity is invalid"
        let rangeRequest = MarketDataRangeRequest(
            expected.ContractVersion, expected.Provider, expected.Symbol, expected.TimeFrame,
            expected.AdjustmentMode, expected.Market, expected.CalendarVersion,
            expected.RequestedFromUtc, expected.RequestedToUtc)
        let! current = this.ReadRangeAsync(rangeRequest, ct)
        let matches =
            current.Evidence.IsComplete = expected.IsComplete
            && String.Equals(current.Evidence.ContentHash, expected.ContentHash, StringComparison.Ordinal)
        return MarketDataEvidenceVerificationResponse(
            MarketDataContractVersions.Current, expected.EvidenceId, matches,
            current.Evidence.Revision, current.Evidence.ContentHash,
            (if matches then null else "market-data-evidence-content-or-completeness-mismatch"))
    }

    member _.ReadExecutionWindowAsync(request: MarketDataExecutionWindowRequest, ct: CancellationToken) = task {
        ContractPolicy.validateVersion request.ContractVersion
        if request.RequiredBars < 1 || request.RequiredBars > MarketDataExecutionEvidenceLimits.MaximumBars then
            invalidArg "requiredBars" $"RequiredBars must be between 1 and {MarketDataExecutionEvidenceLimits.MaximumBars}"
        if request.AfterRevision < 0L
            || (request.AfterRevision > 0L && not request.EvaluatedThroughUtc.HasValue) then
            invalidArg "evaluationWatermark" "A positive revision requires an evaluated-through timestamp"
        let provider = ContractPolicy.normalizeProvider request.Provider
        let frame = ContractPolicy.normalizeTimeFrame request.TimeFrame
        if frame <> TimeFrame.Daily then
            invalidArg "timeFrame" "Autonomous execution evidence currently supports completed daily bars only"
        let descriptor = DataProviderCatalog.Get(provider)
        if request.Market <> descriptor.Market then
            invalidArg "market" "Execution evidence market does not match the provider catalog"
        if request.CalendarVersion <> ExchangeCalendarCatalog.Version then
            invalidArg "calendarVersion" "Execution evidence requires the active market calendar version"
        ContractPolicy.normalizeAdjustment provider frame request.AdjustmentMode |> ignore
        let symbol = ContractPolicy.normalizeSymbol request.Symbol
        let fromUtc, toUtc = ContractPolicy.ensureRange request.NotBeforeUtc request.CompletedThroughUtc
        use connection = new SqliteConnection(connectionString)
        do! connection.OpenAsync(ct)
        use cmd = command connection None """
SELECT Symbol,TimeFrame,TimestampUtc,Open,High,Low,Close,Volume,Vwap FROM (
 SELECT Symbol,TimeFrame,TimestampUtc,Open,High,Low,Close,Volume,Vwap FROM Bars
 WHERE Provider=@provider AND Symbol=@symbol AND TimeFrame=@frame AND AdjustmentMode=@adjustment
  AND TimestampUtc>=@from AND TimestampUtc<=@to
 ORDER BY TimestampUtc DESC LIMIT @required)
ORDER BY TimestampUtc"""
        parameter cmd "@provider" (provider.ToString())
        parameter cmd "@symbol" symbol
        parameter cmd "@frame" (frame.ToString())
        parameter cmd "@adjustment" request.AdjustmentMode
        parameter cmd "@from" (fromUtc.ToString("O", invariant))
        parameter cmd "@to" (toUtc.ToString("O", invariant))
        parameter cmd "@required" (box request.RequiredBars)
        use! reader = cmd.ExecuteReaderAsync(ct)
        let bars = ResizeArray<MarketDataBar>()
        while reader.Read() do bars.Add(readBar reader)
        reader.Close()
        use revisionCmd = command connection None "SELECT CAST(Value AS INTEGER) FROM Metadata WHERE Key='LatestRevision'"
        let! revision = scalarInt64 revisionCmd ct
        use completeCmd = command connection None """
SELECT COUNT(*) FROM EvidenceRanges WHERE Provider=@provider AND Symbol=@symbol AND TimeFrame=@frame
 AND AdjustmentMode=@adjustment AND ToUtc>=@expected AND IsComplete=1"""
        parameter completeCmd "@provider" (provider.ToString())
        parameter completeCmd "@symbol" symbol
        parameter completeCmd "@frame" (frame.ToString())
        parameter completeCmd "@adjustment" request.AdjustmentMode
        parameter completeCmd "@expected" (
            request.ExpectedLastSessionDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc).ToString("O", invariant))
        let! completeRanges = scalarInt64 completeCmd ct
        let hash = ContractPolicy.contentHash bars
        let zone = TimeZoneInfo.FindSystemTimeZoneById(
            MarketRegionCatalog.Get(descriptor.MarketRegion).TimeZoneId)
        let sessionDates = HashSet<DateOnly>(
            bars
            |> Seq.map (fun bar ->
                DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(
                    ContractPolicy.utc bar.TimestampUtc, zone))))
        let expectedSessionPresent = sessionDates.Contains request.ExpectedLastSessionDate
        let contiguousCompletedSessions =
            if bars.Count = 0 then false
            else
                let mutable date = sessionDates |> Seq.min
                let mutable complete = true
                while complete && date <= request.ExpectedLastSessionDate do
                    let tradingDay = ExchangeCalendarCatalog.GetTradingDay(
                        descriptor.MarketRegion, date)
                    if tradingDay.IsTradingDay && not (sessionDates.Contains date) then
                        complete <- false
                    date <- date.AddDays 1
                complete
        let evidence = MarketDataEvidenceContract(
            MarketDataContractVersions.Current,
            ContractPolicy.evidenceId (provider.ToString()) symbol (frame.ToString()) request.AdjustmentMode request.CalendarVersion revision hash,
            provider.ToString(), symbol, frame.ToString(), request.AdjustmentMode, request.Market,
            request.CalendarVersion, fromUtc, toUtc,
            (if bars.Count = 0 then Nullable() else Nullable(bars[0].TimestampUtc)),
            (if bars.Count = 0 then Nullable() else Nullable(bars[bars.Count - 1].TimestampUtc)),
            revision, completeRanges > 0L && bars.Count = request.RequiredBars
                && expectedSessionPresent && contiguousCompletedSessions, hash)
        let mutable priorCorrected = false
        if request.AfterRevision > 0L then
            use correction = command connection None """
SELECT COUNT(*) FROM Corrections WHERE Revision>@revision AND Provider=@provider
 AND Symbol=@symbol AND TimeFrame=@frame AND AdjustmentMode=@adjustment
 AND TimestampUtc<=@evaluated"""
            parameter correction "@revision" (box request.AfterRevision)
            parameter correction "@provider" (provider.ToString())
            parameter correction "@symbol" symbol
            parameter correction "@frame" (frame.ToString())
            parameter correction "@adjustment" request.AdjustmentMode
            parameter correction "@evaluated" (
                ContractPolicy.utc(request.EvaluatedThroughUtc.Value).ToString("O", invariant))
            let! correctionCount = scalarInt64 correction ct
            priorCorrected <- correctionCount > 0L
        return MarketDataExecutionWindowResponse(
            evidence, bars.ToArray(), priorCorrected)
    }

    member _.CorrectionsAsync(afterRevision: int64, limit: int, ct: CancellationToken) = task {
        let bounded = Math.Clamp(limit, 1, 1000)
        use connection = new SqliteConnection(connectionString)
        do! connection.OpenAsync(ct)
        use cmd = command connection None """
SELECT Revision,Provider,Symbol,TimeFrame,AdjustmentMode,TimestampUtc,PreviousHash,CurrentHash,OccurredAtUtc
FROM Corrections WHERE Revision>@after ORDER BY Revision,Sequence LIMIT @limit"""
        parameter cmd "@after" (box afterRevision)
        parameter cmd "@limit" (box bounded)
        use! reader = cmd.ExecuteReaderAsync(ct)
        let values = ResizeArray<MarketDataCorrection>()
        while reader.Read() do
            values.Add(MarketDataCorrection(
                reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                reader.GetString(4), DateTime.Parse(reader.GetString(5), invariant, DateTimeStyles.RoundtripKind),
                reader.GetString(6), reader.GetString(7),
                DateTime.Parse(reader.GetString(8), invariant, DateTimeStyles.RoundtripKind)))
        reader.Close()
        use revisionCmd = command connection None "SELECT CAST(Value AS INTEGER) FROM Metadata WHERE Key='LatestRevision'"
        let! latest = scalarInt64 revisionCmd ct
        return MarketDataCorrectionPage(latest, values.ToArray())
    }

    member _.StatusAsync(ct: CancellationToken) = task {
        use connection = new SqliteConnection(connectionString)
        do! connection.OpenAsync(ct)
        use countCmd = command connection None "SELECT COUNT(*) FROM Bars"
        let! count = scalarInt64 countCmd ct
        use revisionCmd = command connection None "SELECT CAST(Value AS INTEGER) FROM Metadata WHERE Key='LatestRevision'"
        let! revision = scalarInt64 revisionCmd ct
        return count, revision
    }

    member _.SeriesAsync(ct: CancellationToken) = task {
        use connection = new SqliteConnection(connectionString)
        do! connection.OpenAsync(ct)
        use cmd = command connection None """
SELECT Provider,Symbol,TimeFrame,AdjustmentMode,MIN(TimestampUtc),MAX(TimestampUtc),COUNT(*),MAX(Revision)
FROM Bars GROUP BY Provider,Symbol,TimeFrame,AdjustmentMode ORDER BY Provider,Symbol,TimeFrame"""
        use! reader = cmd.ExecuteReaderAsync(ct)
        let values = ResizeArray<MarketDataStoredSeries>()
        while reader.Read() do
            values.Add(MarketDataStoredSeries(
                reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                DateTime.Parse(reader.GetString(4), invariant, DateTimeStyles.RoundtripKind),
                DateTime.Parse(reader.GetString(5), invariant, DateTimeStyles.RoundtripKind),
                reader.GetInt64(6), reader.GetInt64(7)))
        return MarketDataStoredSeriesResponse(values.ToArray())
    }
