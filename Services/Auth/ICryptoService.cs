namespace StockTrader.Services.Auth;

/// <summary>
/// Provides symmetric encryption/decryption for sensitive data stored in the database.
/// Implementation uses AES-256-GCM authenticated encryption.
/// </summary>
public interface ICryptoService
{
    /// <summary>
    /// Encrypts plaintext using AES-256-GCM.
    /// Returns a Base64-encoded string containing nonce + ciphertext + tag.
    /// </summary>
    string Encrypt(string plaintext);

    /// <summary>
    /// Decrypts a value previously encrypted by <see cref="Encrypt"/>.
    /// Returns the original plaintext, or throws on tampered/invalid data.
    /// </summary>
    string Decrypt(string ciphertext);

    /// <summary>
    /// Returns true if the master key is present and the service is operational.
    /// When false, sensitive fields should be stored as-is (plaintext) with a warning.
    /// </summary>
    bool IsAvailable { get; }
}
