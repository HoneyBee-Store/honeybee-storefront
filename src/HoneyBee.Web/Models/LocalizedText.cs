using System.Globalization;

namespace HoneyBee.Web.Models;

/// <summary>
/// Picks the right language column for content that lives in the database.
/// UI chrome goes through IStringLocalizer instead — this is only for text the
/// shop owner edits.
/// </summary>
public static class LocalizedText
{
    public static bool IsArabic =>
        CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ar";

    /// <summary>Falls back to the other language rather than rendering blank.</summary>
    private static string Pick(string? ar, string? en)
    {
        var preferred = IsArabic ? ar : en;
        if (!string.IsNullOrWhiteSpace(preferred)) return preferred;

        var fallback = IsArabic ? en : ar;
        return string.IsNullOrWhiteSpace(fallback) ? "" : fallback;
    }

    public static string Name(this Product p) => Pick(p.NameAr, p.NameEn);
    public static string Description(this Product p) => Pick(p.DescriptionAr, p.DescriptionEn);
    public static string Unit(this Product p) => Pick(p.UnitAr, p.UnitEn);

    public static string Alt(this ProductImage i) => Pick(i.AltAr, i.AltEn);

    public static string Name(this PickupLocation l) => Pick(l.NameAr, l.NameEn);
}
