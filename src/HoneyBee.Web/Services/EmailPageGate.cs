using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace HoneyBee.Web.Services;

/// <summary>
/// A second passphrase in front of the mail settings page.
///
/// The owner is already signed in to reach it; this guards the case of a
/// session left open on an unattended machine, where the page would otherwise
/// let someone change where order notifications are sent.
///
/// Enforced on the server, not with a JavaScript prompt: the page and the
/// settings on it are only ever rendered after the passphrase is accepted,
/// so there is nothing to reveal by skipping the dialog.
/// </summary>
public class EmailPageGate
{
    /// <summary>
    /// Outer bound on one unlock. In practice it usually ends sooner: leaving
    /// the mail pages for anywhere else in the admin drops it immediately.
    /// This only covers sitting on the page itself.
    /// </summary>
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    private const int MaxAttempts = 5;
    private static readonly TimeSpan LockoutFor = TimeSpan.FromMinutes(10);

    private const string KeyUnlockedAt = "email-gate:unlocked-at";
    private const string KeyAttempts   = "email-gate:attempts";
    private const string KeyLockedTo   = "email-gate:locked-until";

    private readonly string? _passphrase;
    private readonly ILogger<EmailPageGate> _log;

    public EmailPageGate(IConfiguration configuration, ILogger<EmailPageGate> log)
    {
        _passphrase = configuration["Admin:EmailPassphrase"];
        _log = log;
    }

    /// <summary>
    /// False when no passphrase is configured — the gate then stays out of the
    /// way rather than locking the owner out of their own settings.
    /// </summary>
    public bool IsEnabled => !string.IsNullOrWhiteSpace(_passphrase);

    public bool IsUnlocked(ISession session)
    {
        if (!IsEnabled) return true;

        var stamp = session.GetString(KeyUnlockedAt);

        // Invariant culture on purpose. Request localization sets Arabic as the
        // default culture for every request, and parsing a round-trip timestamp
        // under the ambient culture is a good way to silently fail and leave the
        // page permanently locked.
        if (!DateTimeOffset.TryParseExact(
                stamp, "O", CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out var unlockedAt))
        {
            return false;
        }

        return DateTimeOffset.UtcNow - unlockedAt < Lifetime;
    }

    public TimeSpan? LockedFor(ISession session)
    {
        var until = session.GetString(KeyLockedTo);

        if (!DateTimeOffset.TryParseExact(
                until, "O", CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out var lockedUntil))
        {
            return null;
        }

        var remaining = lockedUntil - DateTimeOffset.UtcNow;
        return remaining > TimeSpan.Zero ? remaining : null;
    }

    /// <summary>
    /// Checks an attempt and records the outcome. Returns false both for a
    /// wrong passphrase and while locked out.
    /// </summary>
    public bool TryUnlock(ISession session, string? attempt)
    {
        if (!IsEnabled) return true;
        if (LockedFor(session) is not null) return false;

        // Constant-time: a plain == on strings returns as soon as two bytes
        // differ, which leaks how much of the passphrase was right.
        var expected = Encoding.UTF8.GetBytes(_passphrase!);
        var given = Encoding.UTF8.GetBytes(attempt ?? "");

        if (!CryptographicOperations.FixedTimeEquals(expected, given))
        {
            var attempts = session.GetInt32(KeyAttempts) ?? 0;
            attempts++;
            session.SetInt32(KeyAttempts, attempts);

            if (attempts >= MaxAttempts)
            {
                session.SetString(KeyLockedTo,
                    DateTimeOffset.UtcNow.Add(LockoutFor).ToString("O", CultureInfo.InvariantCulture));
                session.SetInt32(KeyAttempts, 0);
                _log.LogWarning("Mail settings passphrase locked out after {Count} attempts.", attempts);
            }

            return false;
        }

        session.SetString(KeyUnlockedAt, DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        session.Remove(KeyAttempts);
        session.Remove(KeyLockedTo);
        return true;
    }

    /// <summary>Ends the unlock early, for the Lock button.</summary>
    public void Lock(ISession session) => session.Remove(KeyUnlockedAt);

    public int AttemptsLeft(ISession session) =>
        Math.Max(0, MaxAttempts - (session.GetInt32(KeyAttempts) ?? 0));
}
