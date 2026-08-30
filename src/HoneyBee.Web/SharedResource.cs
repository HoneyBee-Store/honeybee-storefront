namespace HoneyBee.Web;

/// <summary>
/// Marker type for the shared string table. Views resolve UI text through
/// <c>IStringLocalizer&lt;SharedResource&gt;</c>.
///
/// The English wording is used as the lookup key, so there is no English
/// .resx — a miss falls back to the key, which is already correct English.
/// Only Resources/SharedResource.ar.resx exists.
///
/// Product names and descriptions are NOT here: they are per-language columns
/// on the entities, because shop staff edit them, not developers.
/// </summary>
public class SharedResource
{
}
