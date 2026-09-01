namespace StockTrader.TradingCoreService

open System
open System.Security.Cryptography
open System.Text

type ProtectedPayload = { Ciphertext: byte array; Nonce: byte array; Tag: byte array }

module SecretProtection =
    let protect (key: byte array) (plaintext: byte array) =
        let nonce = RandomNumberGenerator.GetBytes 12
        let ciphertext = Array.zeroCreate<byte> plaintext.Length
        let tag = Array.zeroCreate<byte> 16
        use aes = new AesGcm(key, 16)
        aes.Encrypt(nonce, plaintext, ciphertext, tag)
        { Ciphertext = ciphertext; Nonce = nonce; Tag = tag }

    let unprotect (key: byte array) (payload: ProtectedPayload) =
        let plaintext = Array.zeroCreate<byte> payload.Ciphertext.Length
        use aes = new AesGcm(key, 16)
        aes.Decrypt(payload.Nonce, payload.Ciphertext, payload.Tag, plaintext)
        plaintext

type SecretStore(config: ServiceConfig) =
    member _.KeyGeneration = config.EncryptionKeyGeneration

    member _.Protect(value: string) =
        let plaintext = Encoding.UTF8.GetBytes value
        try SecretProtection.protect config.EncryptionKey plaintext
        finally CryptographicOperations.ZeroMemory plaintext

    member _.Unprotect(payload: ProtectedPayload, keyGeneration: string) =
        if not (String.Equals(keyGeneration, config.EncryptionKeyGeneration, StringComparison.Ordinal)) then
            invalidOp "trading-core-encryption-key-generation-mismatch"
        let plaintext = SecretProtection.unprotect config.EncryptionKey payload
        try Encoding.UTF8.GetString plaintext
        finally CryptographicOperations.ZeroMemory plaintext
