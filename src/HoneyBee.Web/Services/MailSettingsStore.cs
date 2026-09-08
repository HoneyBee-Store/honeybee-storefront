using HoneyBee.Web.Data;
using HoneyBee.Web.Models;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace HoneyBee.Web.Services;

/// <summary>
/// Reads and writes the mail settings the owner edits in the admin.
///
/// They live in the database rather than a config file for two reasons: the
/// owner can change them on a deployed server without touching the host's
/// environment variables, and appsettings.json is committed to git — a password
/// put there would end up on GitHub.
///
/// The password is encrypted with ASP.NET Core Data Protection before it is
/// stored, so reading the Settings table does not reveal it.
/// </summary>
public class MailSettingsStore
{
    private const string KeyHost     = "Smtp.Host";
    private const string KeyPort     = "Smtp.Port";
    private const string KeyUser     = "Smtp.User";
    private const string KeyFrom     = "Smtp.From";
    private const string KeyPassword = "Smtp.Password";   // stored encrypted
    private const string KeyProvider = "Mail.Provider";
    private const string KeyApiKey   = "Mail.ApiKey";     // stored encrypted

    private readonly AppDbContext _db;
    private readonly IDataProtector _protector;
    private readonly MailSettings _fromConfig;
    private readonly ILogger<MailSettingsStore> _log;

    public MailSettingsStore(
        AppDbContext db,
        IDataProtectionProvider protection,
        MailSettings fromConfig,
        ILogger<MailSettingsStore> log)
    {
        _db = db;
        // The purpose string scopes the key: a payload protected here cannot be
        // unprotected by any other part of the app.
        _protector = protection.CreateProtector("HoneyBee.Smtp.Password.v1");
        _fromConfig = fromConfig;
        _log = log;
    }

    /// <summary>
    /// The effective settings: whatever is in the database, falling back to
    /// appsettings/environment for anything not set there. That keeps the
    /// existing Smtp__Password environment variable working as a deployment
    /// option for anyone who prefers it.
    /// </summary>
    public async Task<MailSettings> GetAsync(CancellationToken ct = default)
    {
        var rows = await _db.Settings
            .AsNoTracking()
            .Where(s => s.Key.StartsWith("Smtp.") || s.Key.StartsWith("Mail."))
            .ToDictionaryAsync(s => s.Key, s => s.Value, ct);

        string? Pick(string key, string? fallback)
            => rows.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v : fallback;

        var settings = new MailSettings
        {
            Host = Pick(KeyHost, _fromConfig.Host),
            User = Pick(KeyUser, _fromConfig.User),
            From = Pick(KeyFrom, _fromConfig.From),
            TimeoutSeconds = _fromConfig.TimeoutSeconds
        };

        settings.Port = int.TryParse(Pick(KeyPort, null), out var port) ? port : _fromConfig.Port;

        settings.Provider = Enum.TryParse<MailProvider>(Pick(KeyProvider, null), out var provider)
            ? provider
            : _fromConfig.Provider;

        settings.ApiKey = Unprotect(rows, KeyApiKey) ?? _fromConfig.ApiKey;

        settings.Password = Unprotect(rows, KeyPassword) ?? _fromConfig.Password;

        return settings;
    }

    /// <summary>
    /// Decrypts one stored secret, or returns null if it is absent or
    /// unreadable. Unreadable happens when the Data Protection keys are lost —
    /// a fresh deployment, or a keyring directory that was not persisted. The
    /// value must simply be re-entered; nothing else is broken.
    /// </summary>
    private string? Unprotect(IReadOnlyDictionary<string, string> rows, string key)
    {
        if (!rows.TryGetValue(key, out var stored) || string.IsNullOrWhiteSpace(stored))
        {
            return null;
        }

        try
        {
            return _protector.Unprotect(stored);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex,
                "Stored value for {Key} could not be decrypted. Re-enter it in Admin → Email.", key);
            return null;
        }
    }

    /// <summary>
    /// Saves the settings. A blank secret leaves the stored one alone, so the
    /// owner can edit the sender or server without retyping a 16-character app
    /// password or a long API key every time.
    /// </summary>
    public async Task SaveAsync(
        MailProvider provider, string? host, int port, string? user, string? from,
        string? password, string? apiKey, CancellationToken ct = default)
    {
        await SetAsync(KeyProvider, provider.ToString(), ct);
        await SetAsync(KeyHost, host?.Trim(), ct);
        await SetAsync(KeyPort, port.ToString(), ct);
        await SetAsync(KeyUser, user?.Trim(), ct);
        await SetAsync(KeyFrom, from?.Trim(), ct);

        // Whitespace is stripped before encrypting: both of these are routinely
        // pasted with stray spaces, and the provider then rejects them.
        if (!string.IsNullOrWhiteSpace(password))
        {
            await SetAsync(KeyPassword, _protector.Protect(Strip(password)), ct);
        }

        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            await SetAsync(KeyApiKey, _protector.Protect(Strip(apiKey)), ct);
        }

        await _db.SaveChangesAsync(ct);
    }

    private static string Strip(string value) =>
        new(value.Where(c => !char.IsWhiteSpace(c)).ToArray());

    /// <summary>Forgets both stored secrets, switching email off.</summary>
    public async Task ClearSecretsAsync(CancellationToken ct = default)
    {
        await SetAsync(KeyPassword, null, ct);
        await SetAsync(KeyApiKey, null, ct);
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>True when an SMTP password is held in the database.</summary>
    public Task<bool> HasStoredPasswordAsync(CancellationToken ct = default)
        => _db.Settings.AnyAsync(s => s.Key == KeyPassword && s.Value != "", ct);

    /// <summary>True when a Brevo API key is held in the database.</summary>
    public Task<bool> HasStoredApiKeyAsync(CancellationToken ct = default)
        => _db.Settings.AnyAsync(s => s.Key == KeyApiKey && s.Value != "", ct);

    private async Task SetAsync(string key, string? value, CancellationToken ct)
    {
        var row = await _db.Settings.FirstOrDefaultAsync(s => s.Key == key, ct);

        if (string.IsNullOrWhiteSpace(value))
        {
            if (row is not null) _db.Settings.Remove(row);
            return;
        }

        if (row is null) _db.Settings.Add(new Setting { Key = key, Value = value });
        else row.Value = value;
    }
}
