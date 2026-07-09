using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using TicketMaster.ServiceDefaults.Configuration.Secrets;
using Xunit;

namespace TicketMaster.UnitTests.Configuration;

public class SecretConfigurationTests
{
    private static AesGcmEncryptionProvider NewProvider() => new(RandomNumberGenerator.GetBytes(32));

    [Fact]
    public void Encrypt_then_decrypt_round_trips()
    {
        var crypto = NewProvider();

        var cipher = crypto.Encrypt("s3cr3t-value");

        cipher.Should().NotBe("s3cr3t-value");
        crypto.Decrypt(cipher).Should().Be("s3cr3t-value");
    }

    [Fact]
    public void Tampered_ciphertext_fails_authentication()
    {
        var crypto = NewProvider();
        var bytes = Convert.FromBase64String(crypto.Encrypt("value"));
        bytes[^1] ^= 0xFF; // flip a ciphertext byte
        var tampered = Convert.ToBase64String(bytes);

        Action decrypt = () => crypto.Decrypt(tampered);

        decrypt.Should().Throw<CryptographicException>();
    }

    [Fact]
    public void Wrong_key_size_is_rejected()
    {
        Action ctor = () => _ = new AesGcmEncryptionProvider(new byte[16]);
        ctor.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void AddSecretDecryption_decrypts_prefixed_values_in_place()
    {
        var crypto = NewProvider();
        var config = new ConfigurationManager();
        config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Db:Password"] = SecretConfigurationExtensions.SecretPrefix + crypto.Encrypt("p@ss"),
            ["Db:Host"] = "localhost",
        });

        config.AddSecretConfiguration(() => crypto);

        config["Db:Password"].Should().Be("p@ss");
        config["Db:Host"].Should().Be("localhost");
    }

    [Fact]
    public void AddSecretDecryption_is_a_noop_without_secrets()
    {
        var config = new ConfigurationManager();
        config.AddInMemoryCollection(new Dictionary<string, string?> { ["Db:Host"] = "localhost" });

        // No key set, no secret: values → must not throw.
        config.AddSecretConfiguration(NewProvider);

        config["Db:Host"].Should().Be("localhost");
    }

    [Fact]
    public void Reload_of_underlying_config_re_decrypts_rotated_secret()
    {
        var crypto = NewProvider();
        var source = new ReloadableSource();
        source.Set("Db:Password", SecretConfigurationExtensions.SecretPrefix + crypto.Encrypt("first"));

        var config = new ConfigurationManager();
        ((IConfigurationBuilder)config).Add(source);
        config.AddSecretConfiguration(() => crypto);

        config["Db:Password"].Should().Be("first");

        // Rotate the underlying secret and reload — the decrypted value must update (not stay stale).
        source.Set("Db:Password", SecretConfigurationExtensions.SecretPrefix + crypto.Encrypt("rotated"));
        source.Reload();

        config["Db:Password"].Should().Be("rotated");
    }

    [Fact]
    public void Derive_same_passphrase_and_salt_round_trips()
    {
        var salt = System.Text.Encoding.UTF8.GetBytes("app-salt");
        var encryptor = AesGcmEncryptionProvider.Derive("passphrase", salt);
        var decryptor = AesGcmEncryptionProvider.Derive("passphrase", salt);

        decryptor.Decrypt(encryptor.Encrypt("value")).Should().Be("value");
    }

    [Fact]
    public void Derive_with_different_salt_cannot_decrypt()
    {
        var a = AesGcmEncryptionProvider.Derive("passphrase", System.Text.Encoding.UTF8.GetBytes("salt-A"));
        var b = AesGcmEncryptionProvider.Derive("passphrase", System.Text.Encoding.UTF8.GetBytes("salt-B"));

        var act = () => b.Decrypt(a.Encrypt("value"));

        act.Should().Throw<CryptographicException>();
    }

    [Fact]
    public void Derive_with_different_passphrase_cannot_decrypt()
    {
        var salt = System.Text.Encoding.UTF8.GetBytes("app-salt");
        var a = AesGcmEncryptionProvider.Derive("passphrase-1", salt);
        var b = AesGcmEncryptionProvider.Derive("passphrase-2", salt);

        var act = () => b.Decrypt(a.Encrypt("value"));

        act.Should().Throw<CryptographicException>();
    }

    [Fact]
    public void Secret_in_json_file_is_decrypted()
    {
        const string keyMaterial = "test-key-material";
        var cipher = AesGcmEncryptionProvider.Derive(keyMaterial).Encrypt("S3cr3t-P@ss");
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), System.IO.Path.GetRandomFileName() + ".json");
        System.IO.File.WriteAllText(path,
            $$"""{ "Db": { "Password": "{{SecretConfigurationExtensions.SecretPrefix}}{{cipher}}" } }""");
        try
        {
            var config = new ConfigurationManager();
            config.AddJsonFile(path);
            config.AddSecretConfiguration(() => AesGcmEncryptionProvider.Derive(keyMaterial));

            config["Db:Password"].Should().Be("S3cr3t-P@ss");
        }
        finally
        {
            System.IO.File.Delete(path);
        }
    }

    private sealed class ReloadableSource : IConfigurationSource
    {
        private readonly ReloadableProvider _provider = new();

        public void Set(string key, string? value) => _provider.Set(key, value);
        public void Reload() => _provider.Reload();
        public IConfigurationProvider Build(IConfigurationBuilder builder) => _provider;

        private sealed class ReloadableProvider : ConfigurationProvider
        {
            // Set(key, value) is inherited from ConfigurationProvider.
            public void Reload() => OnReload();
        }
    }
}
