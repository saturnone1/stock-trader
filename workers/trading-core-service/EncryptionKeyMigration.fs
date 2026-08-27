namespace StockTrader.TradingCoreService

open System
open System.IO
open System.Security.Cryptography
open System.Text.Json
open Microsoft.Data.Sqlite
open StockTrader.ServiceContracts.TradingCore

module EncryptionKeyMigration =
    let private json = JsonSerializerOptions(JsonSerializerDefaults.Web)

    let private payload (reader: SqliteDataReader) =
        { Ciphertext = reader.GetFieldValue<byte array>(2)
          Nonce = reader.GetFieldValue<byte array>(3)
          Tag = reader.GetFieldValue<byte array>(4) }

    let private validatePlaintext expectedHash (plaintext: byte array) =
        match JsonSerializer.Deserialize<TradingAccountConfigurationSet>(plaintext, json) |> Option.ofObj with
        | None -> invalidOp "empty-trading-core-account-configuration"
        | Some configuration when not (String.Equals(
                configuration.ConfigurationHash, expectedHash, StringComparison.Ordinal)) ->
            invalidOp "trading-core-account-configuration-hash-mismatch"
        | Some configuration ->
            match TradingCoreCompatibilityPolicy.Error configuration |> Option.ofObj with
            | Some _ -> invalidOp "trading-core-account-configuration-invalid"
            | None -> ()

    let run () =
        let config = Configuration.loadEncryptionMigration ()
        try
            if not (File.Exists config.DatabasePath) then
                invalidOp "trading-core-encryption-migration-database-missing"
            use authorityConnection = Database.connect config.DatabasePath
            use authority = authorityConnection.CreateCommand()
            authority.CommandText <- "SELECT mode FROM authority WHERE singleton=1"
            if String.Equals(Convert.ToString(authority.ExecuteScalar()), "Remote", StringComparison.Ordinal) then
                invalidOp "trading-core-encryption-migration-remote-prohibited"
            authorityConnection.Close()

            Database.initialize config.DatabasePath TradingAuthorityMode.Projection
            use connection = Database.connect config.DatabasePath
            use select = connection.CreateCommand()
            select.CommandText <- "SELECT configuration_hash,encryption_key_generation,ciphertext,nonce,tag FROM account_configuration WHERE singleton=1"
            use reader = select.ExecuteReader()
            if not (reader.Read()) then 0
            else
                let expectedHash = reader.GetString(0)
                let currentGeneration = reader.GetString(1)
                let encrypted = payload reader
                reader.Close()
                if String.Equals(currentGeneration, config.NewGeneration, StringComparison.Ordinal) then
                    let plaintext = SecretProtection.unprotect config.NewKey encrypted
                    try validatePlaintext expectedHash plaintext
                    finally CryptographicOperations.ZeroMemory plaintext
                    0
                else
                    if not (String.Equals(currentGeneration, config.OldGeneration, StringComparison.Ordinal)) then
                        invalidOp "trading-core-encryption-migration-generation-conflict"
                    let plaintext = SecretProtection.unprotect config.OldKey encrypted
                    try
                        validatePlaintext expectedHash plaintext
                        let protectedPayload = SecretProtection.protect config.NewKey plaintext
                        use transaction = connection.BeginTransaction()
                        use update = connection.CreateCommand()
                        update.Transaction <- transaction
                        update.CommandText <- """UPDATE account_configuration
SET ciphertext=$ciphertext,nonce=$nonce,tag=$tag,encryption_key_generation=$new
WHERE singleton=1 AND encryption_key_generation=$old AND configuration_hash=$hash"""
                        update.Parameters.AddWithValue("$ciphertext", protectedPayload.Ciphertext) |> ignore
                        update.Parameters.AddWithValue("$nonce", protectedPayload.Nonce) |> ignore
                        update.Parameters.AddWithValue("$tag", protectedPayload.Tag) |> ignore
                        update.Parameters.AddWithValue("$new", config.NewGeneration) |> ignore
                        update.Parameters.AddWithValue("$old", config.OldGeneration) |> ignore
                        update.Parameters.AddWithValue("$hash", expectedHash) |> ignore
                        if update.ExecuteNonQuery() <> 1 then
                            invalidOp "trading-core-encryption-migration-concurrent-change"
                        use audit = connection.CreateCommand()
                        audit.Transaction <- transaction
                        audit.CommandText <- """INSERT INTO encryption_key_rotation_audit
(rotation_id,old_generation,new_generation,configuration_hash,rotated_at)
VALUES($id,$old,$new,$hash,$at)"""
                        audit.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("N")) |> ignore
                        audit.Parameters.AddWithValue("$old", config.OldGeneration) |> ignore
                        audit.Parameters.AddWithValue("$new", config.NewGeneration) |> ignore
                        audit.Parameters.AddWithValue("$hash", expectedHash) |> ignore
                        audit.Parameters.AddWithValue("$at", DateTime.UtcNow.ToString("O")) |> ignore
                        audit.ExecuteNonQuery() |> ignore
                        transaction.Commit()

                        let roundTrip = SecretProtection.unprotect config.NewKey protectedPayload
                        try validatePlaintext expectedHash roundTrip
                        finally CryptographicOperations.ZeroMemory roundTrip
                        0
                    finally CryptographicOperations.ZeroMemory plaintext
        finally
            CryptographicOperations.ZeroMemory config.OldKey
            CryptographicOperations.ZeroMemory config.NewKey
