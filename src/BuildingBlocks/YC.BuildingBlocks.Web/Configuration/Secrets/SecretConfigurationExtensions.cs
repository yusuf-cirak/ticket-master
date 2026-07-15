using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Primitives;

namespace YC.BuildingBlocks.Web.Configuration.Secrets;

public static class SecretConfigurationExtensions
{
    public const string SecretPrefix = "secret:";

    /// <summary>
    /// Decrypts every configuration value prefixed with <c>secret:</c>. Call after all other sources (appsettings,
    /// env, etc.) are added. Unlike an eager one-shot decrypt, this <b>wraps</b> the sources added so far in a single
    /// decrypting provider that tracks their reload token — so when the underlying configuration reloads (a changed
    /// appsettings file, a refreshed Key Vault/App Config source, a rotated secret), the value is re-decrypted and a
    /// change notification is raised to <c>IOptionsMonitor</c>/<c>IConfiguration</c> consumers. The encryption key is
    /// resolved lazily, only when a <c>secret:</c> value is actually decrypted, so dev setups with no secrets need no key.
    /// </summary>
    public static IConfigurationManager AddSecretConfiguration(
        this IConfigurationManager configuration, Func<IEncryptionProvider> encryptionProviderFactory = null)
    {
        encryptionProviderFactory ??= () => AesGcmEncryptionProvider.FromEnvironment();

        var builder = (IConfigurationBuilder)configuration;

        // Build the already-added sources into an inner root (keeps their file/remote watchers + reload tokens),
        // then replace them with a single provider that reads through the inner and decrypts on the way out.
        var innerBuilder = new ConfigurationBuilder();
        foreach (var source in builder.Sources)
            innerBuilder.Add(source);
        var inner = innerBuilder.Build();

        builder.Sources.Clear();
        builder.Add(new SecretConfigurationSource(inner, encryptionProviderFactory));
        return configuration;
    }
}

internal sealed class SecretConfigurationSource(
    IConfigurationRoot inner,
    Func<IEncryptionProvider> encryptionProviderFactory)
    : IConfigurationSource
{
    public IConfigurationProvider Build(IConfigurationBuilder builder) =>
        new SecretConfigurationProvider(inner, encryptionProviderFactory);
}

/// <summary>
/// Reads all keys from an inner configuration and decrypts <c>secret:</c>-prefixed values, re-decrypting whenever the
/// inner reloads (propagating a change token so consumers see fresh values).
/// </summary>
internal sealed class SecretConfigurationProvider : ConfigurationProvider, IDisposable
{
    private readonly IConfigurationRoot _inner;
    private readonly Lazy<IEncryptionProvider> _encryption;
    private readonly IDisposable _reloadRegistration;

    public SecretConfigurationProvider(IConfigurationRoot inner, Func<IEncryptionProvider> encryptionProviderFactory)
    {
        _inner = inner;
        _encryption = new Lazy<IEncryptionProvider>(encryptionProviderFactory);
        _reloadRegistration = ChangeToken.OnChange(_inner.GetReloadToken, ReloadFromInner);
    }

    public override void Load() => ReloadFromInner();

    private void ReloadFromInner()
    {
        var data = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in _inner.AsEnumerable())
        {
            var value = pair.Value;

            // Only secret:-prefixed values are decrypted; everything else passes through unchanged.
            if (string.IsNullOrEmpty(value) ||
                !value.StartsWith(SecretConfigurationExtensions.SecretPrefix, StringComparison.Ordinal))
            {
                data[pair.Key] = value;
                continue;
            }

            try
            {
                value = _encryption.Value.Decrypt(value[SecretConfigurationExtensions.SecretPrefix.Length..]);
            }
            catch (Exception e)
            {
                // Fail fast — a secret that won't decrypt is a misconfiguration; never let the raw ciphertext
                // (or a half-processed value) reach a consumer.
                throw new InvalidOperationException(
                    $"Failed to decrypt configuration secret for key '{pair.Key}'. Verify the encryption key and salt.", e);
            }

            data[pair.Key] = value;
        }

        Data = data;
        OnReload();
    }

    public void Dispose() => _reloadRegistration?.Dispose();
}