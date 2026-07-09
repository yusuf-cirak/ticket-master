using Microsoft.Extensions.Configuration;

namespace TicketMaster.ServiceDefaults.Configuration.Secrets;

public static class SecretConfigurationExtensions
{
    public const string SecretPrefix = "secret:";

    /// <summary>
    /// Decrypts every configuration value prefixed with <c>secret:</c> in place. Call after all other sources
    /// (appsettings, env, etc.) are added; the decrypted values are layered on top via an in-memory source so they
    /// win. The key is only required when at least one <c>secret:</c> value is present (fail-fast otherwise), so
    /// dev setups with no secrets need no key.
    /// </summary>
    // ponytail: eager, one-shot decrypt at startup — no live-reload of rotated secrets. Add a custom reloading
    // provider only if secret rotation without restart becomes a requirement.
    public static IConfigurationManager AddSecretDecryption(this IConfigurationManager configuration, IEncryptionProvider? encryptionProvider = null)
    {
        var encrypted = configuration.AsEnumerable()
            .Where(kv => kv.Value is { } v && v.StartsWith(SecretPrefix, StringComparison.Ordinal))
            .ToList();

        if (encrypted.Count == 0)
            return configuration;

        encryptionProvider ??= AesGcmEncryptionProvider.FromEnvironment();

        var decrypted = encrypted.ToDictionary(
            kv => kv.Key,
            kv => (string?)encryptionProvider.Decrypt(kv.Value![SecretPrefix.Length..]));

        ((IConfigurationBuilder)configuration).AddInMemoryCollection(decrypted);
        return configuration;
    }
}
