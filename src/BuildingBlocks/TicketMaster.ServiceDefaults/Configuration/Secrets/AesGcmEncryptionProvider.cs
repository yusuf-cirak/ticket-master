using System.Security.Cryptography;
using System.Text;

namespace TicketMaster.ServiceDefaults.Configuration.Secrets;

/// <summary>
/// Symmetric crypto for configuration secrets. Pluggable so the AES-GCM default can later be swapped for a
/// KMS/Key Vault-backed implementation without touching call sites.
/// </summary>
public interface IEncryptionProvider
{
    string Encrypt(string plaintext);
    string Decrypt(string ciphertext);
}



/// <summary>
/// AES-256-GCM authenticated encryption for config secrets. Ciphertext layout (base64): nonce(12) | tag(16) |
/// ciphertext. GCM authenticates the payload, so tampering fails decryption rather than yielding garbage — unlike
/// the plain crypto the pattern this replaces used. The key is a 32-byte value supplied out-of-band (env var),
/// never in the config it protects.
/// </summary>
public sealed class AesGcmEncryptionProvider : IEncryptionProvider
{
    public const string DefaultKeyEnvVar = "CONFIG_SECRET_KEY";
    private const int NonceSize = 12;
    private const int TagSize = 16;

    private readonly byte[] _key;

    public AesGcmEncryptionProvider(byte[] key)
    {
        if (key.Length != 32)
            throw new ArgumentException("AES-256-GCM requires a 32-byte key.", nameof(key));
        _key = key;
    }

    /// <summary>Loads the key from a base64 environment variable. Throws (fail-fast) when it is missing/invalid.</summary>
    public static AesGcmEncryptionProvider FromEnvironment(string envVar = DefaultKeyEnvVar)
    {
        var raw = Environment.GetEnvironmentVariable(envVar);
        if (string.IsNullOrWhiteSpace(raw))
            throw new InvalidOperationException(
                $"Configuration contains encrypted secrets but the '{envVar}' environment variable (base64-encoded 32-byte key) is not set.");
        return new AesGcmEncryptionProvider(Convert.FromBase64String(raw));
    }

    public string Encrypt(string plaintext)
    {
        var plain = Encoding.UTF8.GetBytes(plaintext);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var cipher = new byte[plain.Length];
        var tag = new byte[TagSize];

        using var aes = new AesGcm(_key, TagSize);
        aes.Encrypt(nonce, plain, cipher, tag);

        var output = new byte[NonceSize + TagSize + cipher.Length];
        nonce.CopyTo(output, 0);
        tag.CopyTo(output, NonceSize);
        cipher.CopyTo(output, NonceSize + TagSize);
        return Convert.ToBase64String(output);
    }

    public string Decrypt(string ciphertext)
    {
        var data = Convert.FromBase64String(ciphertext);
        if (data.Length < NonceSize + TagSize)
            throw new FormatException("Ciphertext is too short to contain a nonce and tag.");

        var nonce = data.AsSpan(0, NonceSize);
        var tag = data.AsSpan(NonceSize, TagSize);
        var cipher = data.AsSpan(NonceSize + TagSize);
        var plain = new byte[cipher.Length];

        using var aes = new AesGcm(_key, TagSize);
        aes.Decrypt(nonce, cipher, tag, plain);
        return Encoding.UTF8.GetString(plain);
    }
}
