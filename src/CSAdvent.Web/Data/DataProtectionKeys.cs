using System.Security.Cryptography;
using Azure;
using Azure.Core;
using Azure.Identity;
using Azure.Security.KeyVault.Keys.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.Extensions.Options;

namespace CSAdvent.Web.Data;

public sealed class DataProtectionKeyOptions
{
    public const string SectionName = "DataProtection";

    /// <summary>
    /// The Key Vault key that encrypts the data protection keys before they're saved (#193), versionless, such as
    /// <c>https://postgreskv-abc.vault.azure.net/keys/data-protection</c>. The AppHost sets it in production, where it's
    /// required. Locally and in the tests there's none, and the keys are saved unencrypted.
    /// </summary>
    public string? KeyVaultKeyId { get; set; }

    public Uri? KeyUri => Uri.TryCreate(KeyVaultKeyId, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps ? uri : null;
}

/// <summary>
/// The keys that protect sign-in cookies, antiforgery tokens and the links in emails (unsubscribing, confirming). They live in
/// the database, so a restart or a second replica doesn't sign everyone out or reject their forms. In production each one is
/// encrypted with a Key Vault key before it's saved (#193), so a copy of the database (a restored backup, a dump, a leaked
/// password) can't be used to make an organizer's cookie or someone's unsubscribe link.
/// </summary>
public static class DataProtectionKeys
{
    public const string ApplicationName = "CSAdvent";

    public static WebApplicationBuilder AddDataProtectionKeys(this WebApplicationBuilder builder)
    {
        var dataProtection = builder.Services.AddDataProtection()
            .SetApplicationName(ApplicationName)
            .PersistKeysToDbContext<AdventDbContext>();

        // Checked when the app starts, not here, because EF Core's tools and the migration bundle build the host without it.
        builder.Services.AddOptions<DataProtectionKeyOptions>()
            .BindConfiguration(DataProtectionKeyOptions.SectionName)
            .Validate(
                options => options.KeyVaultKeyId is null || options.KeyUri is not null,
                $"{DataProtectionKeyOptions.SectionName}:KeyVaultKeyId must be a Key Vault key's https URL.")
            .Validate(
                options => builder.Environment.IsDevelopment() || options.KeyUri is not null,
                $"{DataProtectionKeyOptions.SectionName}:KeyVaultKeyId is required outside development (this is {builder.Environment.EnvironmentName}), " +
                "so the data protection keys are encrypted at rest. The AppHost sets it from the Key Vault key it creates.")
            .ValidateOnStart();

        var options = builder.Configuration.GetSection(DataProtectionKeyOptions.SectionName).Get<DataProtectionKeyOptions>();
        if (options?.KeyUri is { } keyUri)
        {
            // The web app's managed identity, which the AppHost lets wrap and unwrap with this key and nothing else. Aspire sets
            // AZURE_CLIENT_ID and AZURE_TOKEN_CREDENTIALS so DefaultAzureCredential picks it.
            var credential = new DefaultAzureCredential();
            dataProtection.ProtectKeysWithAzureKeyVault(keyUri, credential);

            // First among the hosted services, so nothing else starts if the key can't be used.
            builder.Services.Insert(0, ServiceDescriptor.Singleton<IHostedService>(services => new DataProtectionKeyCheck(
                new KeyVaultKeyProbe(keyUri, credential),
                services.GetRequiredService<IKeyManager>(),
                services.GetRequiredService<IOptions<KeyManagementOptions>>(),
                services.GetRequiredService<IDataProtectionProvider>(),
                services.GetRequiredService<TimeProvider>(),
                services.GetRequiredService<ILogger<DataProtectionKeyCheck>>())));
        }

        return builder;
    }
}

/// <summary>Wraps and unwraps a random secret with the Key Vault key, as data protection will.</summary>
public interface IKeyVaultKeyProbe
{
    Uri KeyUri { get; }

    Task ProbeAsync(CancellationToken cancellationToken);
}

internal sealed class KeyVaultKeyProbe(Uri keyUri, TokenCredential credential) : IKeyVaultKeyProbe
{
    public Uri KeyUri => keyUri;

    public async Task ProbeAsync(CancellationToken cancellationToken)
    {
        var secret = RandomNumberGenerator.GetBytes(32);
        var wrapped = await new CryptographyClient(keyUri, credential).WrapKeyAsync(KeyWrapAlgorithm.RsaOaep, secret, cancellationToken);
        var unwrapped = await new CryptographyClient(new Uri(wrapped.KeyId), credential).UnwrapKeyAsync(KeyWrapAlgorithm.RsaOaep, wrapped.EncryptedKey, cancellationToken);
        if (!CryptographicOperations.FixedTimeEquals(secret, unwrapped.Key))
        {
            throw new InvalidOperationException($"Key Vault unwrapped something other than what it wrapped with {keyUri}.");
        }
    }
}

/// <summary>
/// Runs before anything else when the app starts in production, and stops it from starting if data protection can't use its
/// Key Vault key (#193), so a deploy without the key or its role assignment fails instead of serving pages: its new revision
/// never becomes ready, and the Deploy workflow says so. Without this, a replica that can wrap new keys but not unwrap them
/// would quietly make keys nobody can read, signing people out and breaking links. Then it makes sure the key that protects
/// new cookies and links is an encrypted one (<see cref="EncryptedKeys.EnsureNextKeyIsEncrypted"/>), and that the key ring
/// loads and works.
/// </summary>
public sealed class DataProtectionKeyCheck(
    IKeyVaultKeyProbe probe,
    IKeyManager keyManager,
    IOptions<KeyManagementOptions> keyManagement,
    IDataProtectionProvider dataProtection,
    TimeProvider timeProvider,
    ILogger<DataProtectionKeyCheck> logger) : IHostedService
{
    /// <summary>
    /// How long to keep trying the Key Vault key when it's refused. A new role assignment can take a minute or two to reach
    /// Key Vault on the first deploy. Container Apps' startup probe gives the app 100 seconds, then restarts it, so this stays
    /// under that, and a restart tries again.
    /// </summary>
    public TimeSpan KeepTryingFor { get; init; } = TimeSpan.FromSeconds(75);

    public TimeSpan BetweenTries { get; init; } = TimeSpan.FromSeconds(15);

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await ProbeAsync(cancellationToken);

        if (EncryptedKeys.EnsureNextKeyIsEncrypted(keyManager, keyManagement.Value, timeProvider.GetUtcNow()) is { } created)
        {
            logger.LogInformation(
                "Created data protection key {KeyId}, encrypted with Key Vault, to protect new sign-ins and links from {ActivationDate:u}. Keys from before stay readable.",
                created.KeyId,
                created.ActivationDate);
        }

        var protector = dataProtection.CreateProtector("CSAdvent.DataProtection.StartupCheck");
        if (protector.Unprotect(protector.Protect("check")) != "check")
        {
            throw new InvalidOperationException("Data protection didn't give back what it protected.");
        }

        logger.LogInformation("Data protection keys are encrypted with the Key Vault key {KeyUri}.", probe.KeyUri);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task ProbeAsync(CancellationToken cancellationToken)
    {
        var giveUpAt = timeProvider.GetUtcNow() + KeepTryingFor;
        while (true)
        {
            try
            {
                await probe.ProbeAsync(cancellationToken);
                return;
            }
            catch (Exception exception) when ((exception is RequestFailedException or AuthenticationFailedException) && timeProvider.GetUtcNow() < giveUpAt)
            {
                logger.LogWarning(exception, "Data protection can't use the Key Vault key {KeyUri} yet. Trying again.", probe.KeyUri);
                await Task.Delay(BetweenTries, timeProvider, cancellationToken);
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException(
                    $"Data protection can't use the Key Vault key {probe.KeyUri}, so the site won't start. Check that the key exists " +
                    "and is enabled, and that web-identity has the Key Vault Crypto Service Encryption User role on it (infra/dataprotection.bicep).",
                    exception);
            }
        }
    }
}

/// <summary>Which data protection keys are encrypted at rest, and making sure new cookies and links get one that is.</summary>
public static class EncryptedKeys
{
    /// <summary>
    /// How far ahead a new key is made: data protection's own lead, which gives every replica time to load it before it's
    /// used, because each one reloads its keys at least once a day.
    /// </summary>
    public static readonly TimeSpan PropagationWindow = TimeSpan.FromDays(2);

    /// <summary>
    /// The IDs of the keys whose secret is encrypted, as stored. Keys from before #193 aren't, and stay readable: data
    /// protection only decrypts the ones that are.
    /// </summary>
    public static IReadOnlySet<Guid> Ids(IXmlRepository repository) =>
        repository.GetAllElements()
            .Where(element => element.Name.LocalName == "key" && element.Descendants().Any(e => e.Name.LocalName == "encryptedSecret"))
            .Select(element => Guid.TryParse((string?)element.Attribute("id"), out var id) ? id : Guid.Empty)
            .Where(id => id != Guid.Empty)
            .ToHashSet();

    /// <summary>
    /// Keys from before encryption was turned on are only replaced when they expire, 90 days after they were made. So unless an
    /// encrypted key is already current or on its way, this makes one that takes over in <see cref="PropagationWindow"/>,
    /// and from then on every new cookie and link uses it. The unencrypted keys stay, unrevoked, so nobody is signed out and
    /// no link in an email stops working; the runbook says when to revoke them. Returns the new key, or null if none was
    /// needed (also when the current key expires within the window: data protection then makes the next one itself, and it's
    /// encrypted too).
    /// </summary>
    public static IKey? EnsureNextKeyIsEncrypted(IKeyManager keyManager, KeyManagementOptions options, DateTimeOffset now)
    {
        var repository = options.XmlRepository ?? throw new InvalidOperationException("The data protection keys have nowhere to be stored.");
        var encrypted = Ids(repository);
        var keys = keyManager.GetAllKeys();
        if (keys.Any(key => !key.IsRevoked && key.ExpirationDate > now && encrypted.Contains(key.KeyId)))
        {
            return null;
        }

        var current = keys
            .Where(key => !key.IsRevoked && key.ActivationDate <= now && key.ExpirationDate > now)
            .MaxBy(key => key.ActivationDate);
        if (current is null || current.ExpirationDate <= now + PropagationWindow)
        {
            return null;
        }

        var activation = now + PropagationWindow;
        return keyManager.CreateNewKey(activation, activation + options.NewKeyLifetime);
    }
}
