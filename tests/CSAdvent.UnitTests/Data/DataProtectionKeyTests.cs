using System.Globalization;
using System.Text;
using System.Xml.Linq;
using Azure;
using CSAdvent.Web.Data;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.AspNetCore.DataProtection.XmlEncryption;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CSAdvent.UnitTests.Data;

/// <summary>
/// The data protection keys encrypted at rest with a Key Vault key (#193). A stand-in encryptor plays Key Vault, because what
/// matters is how data protection treats the keys from before: they stay readable, and the next one is encrypted.
/// </summary>
public sealed class DataProtectionKeyTests
{
    private const string KeyId = "https://postgreskv-test.vault.azure.net/keys/data-protection";

    [Fact]
    public void KeysFromBeforeEncryptionStayReadableAndTheNextOneIsEncrypted()
    {
        var repository = new MemoryRepository();
        var before = Protector(repository, encrypt: false);
        var signedIn = before.Protector.Protect("signed in before #193");
        var oldKey = Assert.Single(before.Keys.GetAllKeys());
        repository.Backdate(oldKey.KeyId, TimeSpan.FromDays(30));

        // The first start with encryption on, a little over two days ago.
        var after = Protector(repository, encrypt: true);
        var now = DateTimeOffset.UtcNow - EncryptedKeys.PropagationWindow - TimeSpan.FromMinutes(1);
        var created = EncryptedKeys.EnsureNextKeyIsEncrypted(after.Keys, after.Options, now);

        // The cookie from before still works, and nothing about the old key changed.
        Assert.Equal("signed in before #193", after.Protector.Unprotect(signedIn));
        Assert.NotNull(created);
        Assert.Equal(now + EncryptedKeys.PropagationWindow, created.ActivationDate);
        Assert.Equal(created.KeyId, Assert.Single(EncryptedKeys.Ids(repository)));
        Assert.False(after.Keys.GetAllKeys().Single(k => k.KeyId == oldKey.KeyId).IsRevoked);

        // One is enough, however often the app starts.
        Assert.Null(EncryptedKeys.EnsureNextKeyIsEncrypted(after.Keys, after.Options, now));
        Assert.Null(EncryptedKeys.EnsureNextKeyIsEncrypted(Protector(repository, encrypt: true).Keys, after.Options, now.AddHours(1)));

        // Now that it has taken over, new cookies are protected with it, and both kinds still read.
        var later = Protector(repository, encrypt: true);
        var signedInLater = later.Protector.Protect("signed in after #193");
        Assert.Equal(created.KeyId, ProtectedWith(signedInLater));
        Assert.Equal(oldKey.KeyId, ProtectedWith(signedIn));
        Assert.Equal("signed in after #193", later.Protector.Unprotect(signedInLater));
        Assert.Equal("signed in before #193", later.Protector.Unprotect(signedIn));
    }

    [Fact]
    public void ANewDatabaseGetsAnEncryptedKeyFromTheStart()
    {
        var repository = new MemoryRepository();
        var fresh = Protector(repository, encrypt: true);

        // Nothing to replace yet: data protection makes its first key itself, encrypted.
        Assert.Null(EncryptedKeys.EnsureNextKeyIsEncrypted(fresh.Keys, fresh.Options, DateTimeOffset.UtcNow));
        fresh.Protector.Protect("first");
        Assert.Single(EncryptedKeys.Ids(repository));
    }

    [Fact]
    public void TheRunbooksRevocationRevokesOnlyTheKeysFromBeforeEncryption()
    {
        var repository = new MemoryRepository();
        var before = Protector(repository, encrypt: false);
        var signedIn = before.Protector.Protect("old cookie");
        var oldKey = Assert.Single(before.Keys.GetAllKeys());
        repository.Backdate(oldKey.KeyId, TimeSpan.FromDays(30));

        var after = Protector(repository, encrypt: true);
        var encrypted = EncryptedKeys.EnsureNextKeyIsEncrypted(after.Keys, after.Options, DateTimeOffset.UtcNow)!;

        // What the runbook's SQL inserts: a second before the first encrypted key was made, in UTC.
        var revocationDate = encrypted.CreationDate.UtcDateTime.AddSeconds(-1).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        repository.StoreElement(
            XElement.Parse($"""<revocation version="1"><revocationDate>{revocationDate}</revocationDate><key id="*" /><reason>Made before the keys were encrypted at rest (#193)</reason></revocation>"""),
            "revocation-before-encryption");

        var revoked = Protector(repository, encrypt: true);
        var keys = revoked.Keys.GetAllKeys();
        Assert.True(keys.Single(k => k.KeyId == oldKey.KeyId).IsRevoked);
        Assert.False(keys.Single(k => k.KeyId == encrypted.KeyId).IsRevoked);
        Assert.ThrowsAny<System.Security.Cryptography.CryptographicException>(() => revoked.Protector.Unprotect(signedIn));
    }

    [Fact]
    public async Task TheAppWontStartWhenTheKeyVaultKeyCantBeUsed()
    {
        var check = Check(new Probe(failures: int.MaxValue), keepTryingFor: TimeSpan.Zero);

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => check.StartAsync(TestContext.Current.CancellationToken));

        Assert.Contains(KeyId, failure.Message);
        Assert.Contains("Key Vault Crypto Service Encryption User", failure.Message);
        Assert.IsType<RequestFailedException>(failure.InnerException);
    }

    [Fact]
    public async Task ItWaitsForANewRoleAssignmentToReachKeyVault()
    {
        var probe = new Probe(failures: 2);
        var repository = new MemoryRepository();
        var check = Check(probe, keepTryingFor: TimeSpan.FromMinutes(1), repository);

        await check.StartAsync(TestContext.Current.CancellationToken);

        Assert.Equal(3, probe.Tries);
        Assert.Single(EncryptedKeys.Ids(repository));
    }

    [Fact]
    public async Task OnlyKeyVaultRefusalsAreTriedAgain()
    {
        var probe = new Probe(failures: int.MaxValue, error: new UriFormatException("Not a key"));
        var check = Check(probe, keepTryingFor: TimeSpan.FromMinutes(1));

        await Assert.ThrowsAsync<InvalidOperationException>(() => check.StartAsync(TestContext.Current.CancellationToken));
        Assert.Equal(1, probe.Tries);
    }

    [Theory]
    [InlineData("Production", null, false)]
    [InlineData("Production", "http://postgreskv-test.vault.azure.net/keys/data-protection", false)]
    [InlineData("Production", "not a url", false)]
    [InlineData("Production", KeyId, true)]
    [InlineData("Development", null, true)]
    public void ProductionNeedsTheKeyVaultKey(string environment, string? keyId, bool starts)
    {
        using var app = App(environment, keyId).App;

        var options = () => app.Services.GetRequiredService<IOptions<DataProtectionKeyOptions>>().Value;

        if (starts)
        {
            options();
        }
        else
        {
            Assert.Throws<OptionsValidationException>(options);
        }
    }

    [Fact]
    public void WithTheKeyTheKeysAreEncryptedWithKeyVaultAndCheckedFirst()
    {
        var (withKey, checkedFirst) = App("Production", KeyId);
        var (withoutKey, nothingToCheck) = App("Development", keyId: null);
        using var disposeWithKey = withKey;
        using var disposeWithoutKey = withoutKey;

        // The check is the first hosted service, so nothing else starts before it passes.
        Assert.True(checkedFirst);
        Assert.False(nothingToCheck);
        Assert.Contains("AzureKeyVault", withKey.Services.GetRequiredService<IOptions<KeyManagementOptions>>().Value.XmlEncryptor?.GetType().Name);
        Assert.Null(withoutKey.Services.GetRequiredService<IOptions<KeyManagementOptions>>().Value.XmlEncryptor);
        Assert.Equal(DataProtectionKeys.ApplicationName, withKey.Services.GetRequiredService<IOptions<DataProtectionOptions>>().Value.ApplicationDiscriminator);
    }

    /// <summary>The site's data protection setup, and whether a hosted service (the key check) comes before everything else.</summary>
    private static (WebApplication App, bool CheckedFirst) App(string environment, string? keyId)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = environment });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["DataProtection:KeyVaultKeyId"] = keyId });
        builder.AddDataProtectionKeys();
        var checkedFirst = builder.Services[0].ServiceType == typeof(IHostedService);
        return (builder.Build(), checkedFirst);
    }

    /// <summary>The check, at a start after #193's deploy: the keys so far were made unencrypted.</summary>
    private static DataProtectionKeyCheck Check(Probe probe, TimeSpan keepTryingFor, MemoryRepository? repository = null)
    {
        repository ??= new MemoryRepository();
        Protector(repository, encrypt: false).Protector.Protect("signed in before #193");
        var protection = Protector(repository, encrypt: true);
        return new DataProtectionKeyCheck(
            probe,
            protection.Keys,
            Options.Create(protection.Options),
            protection.Provider,
            TimeProvider.System,
            NullLogger<DataProtectionKeyCheck>.Instance)
        {
            KeepTryingFor = keepTryingFor,
            BetweenTries = TimeSpan.Zero,
        };
    }

    /// <summary>The key a payload was protected with: data protection puts its ID after a 4-byte header.</summary>
    private static Guid ProtectedWith(string payload) =>
        new(Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlDecode(payload).AsSpan(4, 16));

    /// <summary>Data protection over the given keys, as the site sets it up, with the stand-in for Key Vault or without.</summary>
    private static Protection Protector(MemoryRepository repository, bool encrypt)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDataProtection().SetApplicationName(DataProtectionKeys.ApplicationName);
        services.Configure<KeyManagementOptions>(options =>
        {
            options.XmlRepository = repository;
            options.XmlEncryptor = encrypt ? new StandInEncryptor() : null;
        });
        var provider = services.BuildServiceProvider();
        return new Protection(
            provider.GetRequiredService<IDataProtectionProvider>(),
            provider.GetRequiredService<IKeyManager>(),
            provider.GetRequiredService<IOptions<KeyManagementOptions>>().Value);
    }

    private sealed record Protection(IDataProtectionProvider Provider, IKeyManager Keys, KeyManagementOptions Options)
    {
        public IDataProtector Protector => Provider.CreateProtector("test");
    }

    private sealed class Probe(int failures, Exception? error = null) : IKeyVaultKeyProbe
    {
        public int Tries { get; private set; }

        public Uri KeyUri { get; } = new(KeyId);

        public Task ProbeAsync(CancellationToken cancellationToken)
        {
            Tries++;
            return Tries <= failures
                ? Task.FromException(error ?? new RequestFailedException(403, "Caller is not authorized to perform action on resource."))
                : Task.CompletedTask;
        }
    }

    /// <summary>The keys in memory, as the database would hold them.</summary>
    private sealed class MemoryRepository : IXmlRepository
    {
        private readonly List<XElement> _elements = [];

        public IReadOnlyCollection<XElement> GetAllElements()
        {
            lock (_elements)
            {
                return [.. _elements.Select(e => new XElement(e))];
            }
        }

        public void StoreElement(XElement element, string friendlyName)
        {
            lock (_elements)
            {
                _elements.Add(new XElement(element));
            }
        }

        /// <summary>Makes a key look older than it is, as if it had been made and used a while before encryption was turned on.</summary>
        public void Backdate(Guid keyId, TimeSpan by)
        {
            lock (_elements)
            {
                var key = _elements.Single(e => (string?)e.Attribute("id") == keyId.ToString());
                foreach (var date in new[] { key.Element("creationDate")!, key.Element("activationDate")! })
                {
                    date.Value = System.Xml.XmlConvert.ToString((DateTimeOffset)date - by);
                }
            }
        }
    }

    /// <summary>Stands in for Key Vault: "encrypts" a key's secret so it isn't stored as it is, and a decryptor reads it back.</summary>
    private sealed class StandInEncryptor : IXmlEncryptor
    {
        public EncryptedXmlInfo Encrypt(XElement plaintextElement) =>
            new(new XElement("standIn", Convert.ToBase64String(Encoding.UTF8.GetBytes(plaintextElement.ToString(SaveOptions.DisableFormatting)))), typeof(StandInDecryptor));
    }

    public sealed class StandInDecryptor : IXmlDecryptor
    {
        public XElement Decrypt(XElement encryptedElement) => XElement.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(encryptedElement.Value)));
    }
}
