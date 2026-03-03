using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using StockTrader.Configuration;

namespace StockTrader.Services.Auth;

/// <summary>
/// AES-256-GCM authenticated encryption.
/// Output format: Base64( nonce[12] || ciphertext[n] || tag[16] )
/// </summary>
public sealed class AesCryptoService : ICryptoService
{
    private const int NonceSizeBytes = 12;  // GCM standard nonce
    private const int TagSizeBytes   = 16;  // GCM authentication tag

    private readonly byte[]? _masterKey;
    private readonly ILogger<AesCryptoService> _logger;

    public bool IsAvailable => _masterKey != null;

    public AesCryptoService(IOptions<SecuritySettings> opts, ILogger<AesCryptoService> logger,
        IConfiguration configuration)
    {
        _logger = logger;

        // Priority: environment variable > appsettings.json
        var rawKey = Environment.GetEnvironmentVariable("STOCKTRADER_MASTER_KEY")
                  ?? opts.Value.MasterEncryptionKey;

        if (string.IsNullOrWhiteSpace(rawKey))
        {
            _logger.LogWarning(
                "STOCKTRADER_MASTER_KEY is not set. Sensitive fields will be stored unencrypted. " +
                "Set the STOCKTRADER_MASTER_KEY environment variable or Security:MasterEncryptionKey in appsettings.json.");
            _masterKey = null;
            return;
        }

        try
        {
            // Accept either a 32-byte Base64 key or a plain passphrase
            var keyBytes = Convert.FromBase64String(rawKey);
            if (keyBytes.Length != 32)
                throw new InvalidOperationException($"Key must be 32 bytes (256-bit). Got {keyBytes.Length} bytes.");
            _masterKey = keyBytes;
        }
        catch (FormatException)
        {
            // Treat as passphrase — derive 32-byte key via SHA-256
            _masterKey = SHA256.HashData(Encoding.UTF8.GetBytes(rawKey));
            _logger.LogWarning(
                "STOCKTRADER_MASTER_KEY is not valid Base64; deriving AES-256 key via SHA-256 from passphrase. " +
                "For best security, use `openssl rand -base64 32` to generate a proper key.");
        }
    }

    public string Encrypt(string plaintext)
    {
        if (_masterKey == null)
            return plaintext; // Passthrough when no key

        var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        var nonce = new byte[NonceSizeBytes];
        RandomNumberGenerator.Fill(nonce);

        var ciphertext = new byte[plaintextBytes.Length];
        var tag = new byte[TagSizeBytes];

        using var aes = new AesGcm(_masterKey, TagSizeBytes);
        aes.Encrypt(nonce, plaintextBytes, ciphertext, tag);

        // Pack: nonce(12) + ciphertext(n) + tag(16)
        var packed = new byte[NonceSizeBytes + ciphertext.Length + TagSizeBytes];
        Buffer.BlockCopy(nonce,       0, packed, 0,                              NonceSizeBytes);
        Buffer.BlockCopy(ciphertext,  0, packed, NonceSizeBytes,                 ciphertext.Length);
        Buffer.BlockCopy(tag,         0, packed, NonceSizeBytes + ciphertext.Length, TagSizeBytes);

        return Convert.ToBase64String(packed);
    }

    public string Decrypt(string ciphertext)
    {
        if (_masterKey == null)
            return ciphertext; // Passthrough when no key

        byte[] packed;
        try
        {
            packed = Convert.FromBase64String(ciphertext);
        }
        catch (FormatException)
        {
            // Value was stored before encryption was enabled — return as-is
            _logger.LogWarning("Decrypt: value is not Base64 — returning raw value (was encryption enabled when stored?)");
            return ciphertext;
        }

        if (packed.Length < NonceSizeBytes + TagSizeBytes)
        {
            _logger.LogWarning("Decrypt: payload too short — returning raw value");
            return ciphertext;
        }

        var nonce      = packed[..NonceSizeBytes];
        var tag        = packed[^TagSizeBytes..];
        var cipherbytes = packed[NonceSizeBytes..^TagSizeBytes];
        var plainbytes  = new byte[cipherbytes.Length];

        using var aes = new AesGcm(_masterKey, TagSizeBytes);
        try
        {
            aes.Decrypt(nonce, cipherbytes, tag, plainbytes);
        }
        catch (CryptographicException ex)
        {
            _logger.LogError(ex,
                "AES-GCM decryption failed — data may be tampered or key mismatch");
            throw new InvalidOperationException(
                "Failed to decrypt sensitive data. Check master encryption key.", ex);
        }

        return Encoding.UTF8.GetString(plainbytes);
    }
}
