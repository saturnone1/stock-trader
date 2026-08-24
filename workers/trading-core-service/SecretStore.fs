namespace StockTrader.TradingCoreService

open System
open System.Security.Cryptography
open System.Text

type ProtectedPayload = { Ciphertext: byte array; Nonce: byte array; Tag: byte array }

type SecretStore(config: ServiceConfig) =
    member _.Protect(value: string) =
        let plaintext = Encoding.UTF8.GetBytes value
        let nonce = RandomNumberGenerator.GetBytes 12
        let ciphertext = Array.zeroCreate<byte> plaintext.Length
        let tag = Array.zeroCreate<byte> 16
        use aes = new AesGcm(config.EncryptionKey, 16)
        aes.Encrypt(nonce, plaintext, ciphertext, tag)
        CryptographicOperations.ZeroMemory plaintext
        { Ciphertext = ciphertext; Nonce = nonce; Tag = tag }

    member _.Unprotect(payload: ProtectedPayload) =
        let plaintext = Array.zeroCreate<byte> payload.Ciphertext.Length
        use aes = new AesGcm(config.EncryptionKey, 16)
        aes.Decrypt(payload.Nonce, payload.Ciphertext, payload.Tag, plaintext)
        try Encoding.UTF8.GetString plaintext
        finally CryptographicOperations.ZeroMemory plaintext
