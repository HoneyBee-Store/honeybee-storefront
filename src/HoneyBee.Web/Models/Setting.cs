using System.ComponentModel.DataAnnotations;

namespace HoneyBee.Web.Models;

/// <summary>
/// A single stored setting, keyed by name.
///
/// A key/value table rather than a typed one-row table: settings are read a
/// handful of times per request at most, and this way a new one needs no
/// migration. Secrets are encrypted before they reach <see cref="Value"/> —
/// see MailSettingsStore.
/// </summary>
public class Setting
{
    [Key]
    [MaxLength(100)]
    public string Key { get; set; } = "";

    /// <summary>
    /// Long enough for a Data Protection payload, which is far bigger than the
    /// plain text it wraps.
    /// </summary>
    [MaxLength(2000)]
    public string Value { get; set; } = "";
}
