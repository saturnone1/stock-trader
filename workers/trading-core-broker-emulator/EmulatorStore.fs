namespace StockTrader.TradingCoreBrokerEmulator

open System
open System.IO
open System.Globalization
open System.Text.Json
open Microsoft.Data.Sqlite
open StockTrader.ServiceContracts
open StockTrader.ServiceContracts.TradingCore

type EmulatorStore(path: string, json: JsonSerializerOptions) as this =
    let connect () =
        let connection = new SqliteConnection($"Data Source={path};Mode=ReadWriteCreate;Cache=Shared")
        connection.Open()
        connection
    do
        Directory.CreateDirectory(Path.GetDirectoryName path) |> ignore
        use connection = connect ()
        use command = connection.CreateCommand()
        command.CommandText <- """
PRAGMA journal_mode=WAL;
CREATE TABLE IF NOT EXISTS state(key TEXT PRIMARY KEY,value TEXT NOT NULL);
CREATE TABLE IF NOT EXISTS calls(sequence INTEGER PRIMARY KEY AUTOINCREMENT,operation TEXT NOT NULL,client_order_id TEXT NULL,request_hash TEXT NOT NULL,observed_at TEXT NOT NULL);
CREATE TABLE IF NOT EXISTS broker_orders(order_id TEXT PRIMARY KEY,client_order_id TEXT NOT NULL UNIQUE,payload_json TEXT NOT NULL,visible_after_barrier TEXT NULL);
CREATE TABLE IF NOT EXISTS barriers(name TEXT PRIMARY KEY,advanced_at TEXT NOT NULL);
"""
        command.ExecuteNonQuery() |> ignore

    let state (connection: SqliteConnection) (key: string) : string option =
        use command = connection.CreateCommand()
        command.CommandText <- "SELECT value FROM state WHERE key=$key"
        command.Parameters.AddWithValue("$key", key) |> ignore
        match command.ExecuteScalar() with null -> None | value -> Some(Convert.ToString(value))

    let setState (connection: SqliteConnection) (transaction: SqliteTransaction)
        (key: string) (value: string) =
        use command = connection.CreateCommand()
        command.Transaction <- transaction
        command.CommandText <- "INSERT INTO state(key,value) VALUES($key,$value) ON CONFLICT(key) DO UPDATE SET value=excluded.value"
        command.Parameters.AddWithValue("$key", key) |> ignore
        command.Parameters.AddWithValue("$value", value) |> ignore
        command.ExecuteNonQuery() |> ignore

    let plan (connection: SqliteConnection) : ScriptedBrokerPlan =
        match state connection "plan" with
        | None -> invalidOp "broker-plan-not-loaded"
        | Some value -> JsonSerializer.Deserialize<ScriptedBrokerPlan>(value, json)

    let decimalValue (value: string) = Decimal.Parse(value, NumberStyles.Number, CultureInfo.InvariantCulture)
    let decimalText (value: decimal) = value.ToString("G29", CultureInfo.InvariantCulture)

    let applyFill (connection: SqliteConnection) (transaction: SqliteTransaction)
        (operation: string) (evidence: ScriptedBrokerOrder) =
        if operation = ScriptedBrokerOperations.SubmitEntry
           || operation = ScriptedBrokerOperations.IncreasePosition
           || operation = ScriptedBrokerOperations.ClosePosition then
            use previousCommand = connection.CreateCommand()
            previousCommand.Transaction <- transaction
            previousCommand.CommandText <- "SELECT payload_json FROM broker_orders WHERE client_order_id=$client"
            previousCommand.Parameters.AddWithValue("$client", evidence.ClientOrderId) |> ignore
            let previousFilled =
                match previousCommand.ExecuteScalar() with
                | null -> 0
                | value ->
                    let previous = JsonSerializer.Deserialize<ScriptedBrokerOrder>(Convert.ToString value, json)
                    previous.FilledQuantity
            let delta = Math.Max(0, evidence.FilledQuantity - previousFilled)
            if delta > 0 then
                let current = JsonSerializer.Deserialize<ScriptedBrokerPosition array>(
                    state connection "positions" |> Option.get, json)
                let price =
                    if String.IsNullOrWhiteSpace evidence.AverageFillPrice then 0M
                    else decimalValue evidence.AverageFillPrice
                let matching = current |> Array.tryFind (fun value ->
                    value.Symbol.Equals(evidence.Symbol, StringComparison.OrdinalIgnoreCase))
                let next =
                    if operation = ScriptedBrokerOperations.ClosePosition then
                        current
                        |> Array.choose (fun value ->
                            if not (value.Symbol.Equals(evidence.Symbol, StringComparison.OrdinalIgnoreCase)) then Some value
                            else
                                let quantity = Math.Max(0, value.Quantity - delta)
                                if quantity = 0 then None
                                else Some(ScriptedBrokerPosition(value.Symbol, quantity,
                                    value.AverageEntryPrice, decimalText price)))
                    else
                        let existingQuantity = matching |> Option.map _.Quantity |> Option.defaultValue 0
                        let existingAverage = matching |> Option.map (fun value -> decimalValue value.AverageEntryPrice) |> Option.defaultValue 0M
                        let quantity = existingQuantity + delta
                        let average =
                            if quantity = 0 then 0M
                            else (existingAverage * decimal existingQuantity + price * decimal delta) / decimal quantity
                        let updated = ScriptedBrokerPosition(evidence.Symbol.ToUpperInvariant(), quantity,
                            decimalText average, decimalText price)
                        Array.append
                            (current |> Array.filter (fun value ->
                                not (value.Symbol.Equals(evidence.Symbol, StringComparison.OrdinalIgnoreCase))))
                            [| updated |]
                        |> Array.sortBy _.Symbol
                setState connection transaction "positions" (JsonSerializer.Serialize(next, json))
                let account = JsonSerializer.Deserialize<ScriptedBrokerAccount>(
                    state connection "account" |> Option.get, json)
                let signed = if operation = ScriptedBrokerOperations.ClosePosition then 1M else -1M
                let cash = decimalValue account.Cash + signed * price * decimal delta
                let marketValue = next |> Seq.sumBy (fun value ->
                    decimal value.Quantity * decimalValue value.CurrentPrice)
                let updatedAccount = ScriptedBrokerAccount(account.AccountId,
                    decimalText (cash + marketValue), account.PreviousDayEquity,
                    decimalText cash, decimalText cash, account.IsTradingBlocked,
                    evidence.FilledAtUtc.GetValueOrDefault(evidence.SubmittedAtUtc))
                setState connection transaction "account" (JsonSerializer.Serialize(updatedAccount, json))

    member _.LoadPlan(value: ScriptedBrokerPlan) =
        match Option.ofObj (TradingCoreAcceptancePolicy.PlanError value) with
        | Some error -> invalidArg "plan" error
        | None ->
            use connection = connect ()
            match state connection "plan" with
            | Some existing ->
                let stored = JsonSerializer.Deserialize<ScriptedBrokerPlan>(existing, json)
                if stored.PlanHash <> value.PlanHash then invalidOp "broker-plan-identity-conflict"
                false
            | None ->
                use transaction = connection.BeginTransaction()
                setState connection transaction "plan" (JsonSerializer.Serialize(value, json))
                setState connection transaction "account" (JsonSerializer.Serialize(value.InitialAccount, json))
                setState connection transaction "positions" (JsonSerializer.Serialize(value.InitialPositions, json))
                transaction.Commit()
                true

    member _.Execute(operation: string, clientOrderId: string, requestHash: string) =
        use connection = connect ()
        let current = plan connection
        use ordinalCommand = connection.CreateCommand()
        ordinalCommand.CommandText <- "SELECT COUNT(*) FROM calls WHERE operation=$operation"
        ordinalCommand.Parameters.AddWithValue("$operation", operation) |> ignore
        let ordinal = Convert.ToInt32(ordinalCommand.ExecuteScalar())
        let step = current.Steps |> Seq.tryFind (fun value ->
            value.Operation = operation && value.CallOrdinal = ordinal
            && (String.IsNullOrWhiteSpace value.ClientOrderId || value.ClientOrderId = clientOrderId))
        match step with
        | None -> invalidOp "unexpected-scripted-broker-call"
        | Some step ->
            use transaction = connection.BeginTransaction()
            use call = connection.CreateCommand()
            call.Transaction <- transaction
            call.CommandText <- "INSERT INTO calls(operation,client_order_id,request_hash,observed_at) VALUES($operation,$client,$hash,$at)"
            call.Parameters.AddWithValue("$operation", operation) |> ignore
            call.Parameters.AddWithValue("$client", if String.IsNullOrWhiteSpace clientOrderId then box DBNull.Value else box clientOrderId) |> ignore
            call.Parameters.AddWithValue("$hash", requestHash) |> ignore
            call.Parameters.AddWithValue("$at", DateTime.UtcNow.ToString("O")) |> ignore
            call.ExecuteNonQuery() |> ignore
            let effectActions: string list =
                [ ScriptedBrokerActions.RecordThenReturn; ScriptedBrokerActions.RecordThenTimeout
                  ScriptedBrokerActions.DelayVisibilityUntilBarrier; ScriptedBrokerActions.ReturnDuplicateEvidence
                  ScriptedBrokerActions.ReturnOutOfOrderEvidence ]
            if effectActions |> List.contains step.Action then
                if isNull step.Evidence then invalidOp "scripted-broker-evidence-missing"
                applyFill connection transaction operation step.Evidence
                use order = connection.CreateCommand()
                order.Transaction <- transaction
                order.CommandText <- "INSERT INTO broker_orders(order_id,client_order_id,payload_json,visible_after_barrier) VALUES($id,$client,$payload,$barrier) ON CONFLICT(client_order_id) DO UPDATE SET payload_json=excluded.payload_json,visible_after_barrier=excluded.visible_after_barrier"
                order.Parameters.AddWithValue("$id", step.Evidence.OrderId) |> ignore
                order.Parameters.AddWithValue("$client", step.Evidence.ClientOrderId) |> ignore
                order.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(step.Evidence, json)) |> ignore
                order.Parameters.AddWithValue("$barrier", if step.Action = ScriptedBrokerActions.DelayVisibilityUntilBarrier then box step.Barrier else box DBNull.Value) |> ignore
                order.ExecuteNonQuery() |> ignore
            transaction.Commit()
            if step.Action = ScriptedBrokerActions.ThrowWithoutEffect then
                invalidOp "scripted-broker-failure-before-effect"
            if step.Action = ScriptedBrokerActions.RecordThenTimeout then
                raise (TimeoutException "scripted-broker-timeout-after-effect")
            if step.Action = ScriptedBrokerActions.EnterOutageUntilBarrier then
                if String.IsNullOrWhiteSpace step.Barrier || not (this.BarrierAdvanced step.Barrier) then
                    invalidOp "scripted-broker-outage"
            step.Evidence

    member _.BarrierAdvanced(name: string) =
        use connection = connect ()
        use command = connection.CreateCommand()
        command.CommandText <- "SELECT COUNT(*) FROM barriers WHERE name=$name"
        command.Parameters.AddWithValue("$name", name) |> ignore
        Convert.ToInt32(command.ExecuteScalar()) = 1

    member _.AdvanceBarrier(name: string) =
        if String.IsNullOrWhiteSpace name then invalidArg "name" "invalid-broker-barrier"
        use connection = connect ()
        use command = connection.CreateCommand()
        command.CommandText <- "INSERT OR IGNORE INTO barriers(name,advanced_at) VALUES($name,$at)"
        command.Parameters.AddWithValue("$name", name) |> ignore
        command.Parameters.AddWithValue("$at", DateTime.UtcNow.ToString("O")) |> ignore
        command.ExecuteNonQuery() = 1

    member _.Account() =
        use connection = connect ()
        this.Execute(ScriptedBrokerOperations.GetAccount, "", CanonicalJsonHash.Compute("account")) |> ignore
        JsonSerializer.Deserialize<ScriptedBrokerAccount>(state connection "account" |> Option.get, json)

    member _.Positions() =
        use connection = connect ()
        this.Execute(ScriptedBrokerOperations.GetPositions, "", CanonicalJsonHash.Compute("positions")) |> ignore
        JsonSerializer.Deserialize<ScriptedBrokerPosition array>(state connection "positions" |> Option.get, json)

    member _.Orders(fromUtc: DateTime, toUtc: DateTime) =
        this.Execute(ScriptedBrokerOperations.GetOrders, "", CanonicalJsonHash.Compute {| fromUtc = fromUtc; toUtc = toUtc |}) |> ignore
        use connection = connect ()
        use command = connection.CreateCommand()
        command.CommandText <- """SELECT payload_json FROM broker_orders o WHERE o.visible_after_barrier IS NULL OR EXISTS(SELECT 1 FROM barriers b WHERE b.name=o.visible_after_barrier) ORDER BY o.order_id"""
        use reader = command.ExecuteReader()
        [| while reader.Read() do yield JsonSerializer.Deserialize<ScriptedBrokerOrder>(reader.GetString 0, json) |]

    member _.Journal() =
        use connection = connect ()
        use command = connection.CreateCommand()
        command.CommandText <- "SELECT operation,client_order_id,request_hash,observed_at FROM calls ORDER BY sequence"
        use reader = command.ExecuteReader()
        [| while reader.Read() do
            yield ScriptedBrokerCall(reader.GetString 0,
                (if reader.IsDBNull 1 then null else reader.GetString 1), reader.GetString 2,
                DateTime.Parse(reader.GetString 3, null, Globalization.DateTimeStyles.RoundtripKind)) |]

    member _.TerminalState() =
        use connection = connect ()
        let account = JsonSerializer.Deserialize<ScriptedBrokerAccount>(
            state connection "account" |> Option.get, json)
        let positions = JsonSerializer.Deserialize<ScriptedBrokerPosition array>(
            state connection "positions" |> Option.get, json)
        use orderCommand = connection.CreateCommand()
        orderCommand.CommandText <- """SELECT payload_json FROM broker_orders o
WHERE o.visible_after_barrier IS NULL OR EXISTS(
 SELECT 1 FROM barriers b WHERE b.name=o.visible_after_barrier)
ORDER BY o.order_id"""
        use orderReader = orderCommand.ExecuteReader()
        let orders =
            [| while orderReader.Read() do
                yield JsonSerializer.Deserialize<ScriptedBrokerOrder>(orderReader.GetString 0, json) |]
        orderReader.Close()
        let journal = this.Journal()
        let candidate = ScriptedBrokerTerminalState(account, positions, orders, journal, "")
        ScriptedBrokerTerminalState(account, positions, orders, journal,
            TradingCoreAcceptanceIdentity.BrokerState candidate)
