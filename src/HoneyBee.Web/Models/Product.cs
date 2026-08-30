using System.ComponentModel.DataAnnotations;

namespace HoneyBee.Web.Models;

/// <summary>
/// A product on the shelf. Text is stored per language rather than through
/// resource files, because this content is edited in the admin panel rather
/// than shipped with the build.
/// </summary>
public class Product
{
    public int Id { get; set; }

    /// <summary>URL segment, e.g. "sidr-honey". Unique, lowercase, no spaces.</summary>
    [Required, MaxLength(80)]
    public string Slug { get; set; } = "";

    [Required, MaxLength(120)]
    public string NameAr { get; set; } = "";

    [Required, MaxLength(120)]
    public string NameEn { get; set; } = "";

    [MaxLength(2000)]
    public string? DescriptionAr { get; set; }

    [MaxLength(2000)]
    public string? DescriptionEn { get; set; }

    /// <summary>
    /// Nullable on purpose: the shop launches without prices, so the storefront
    /// shows "price on request" and orders are confirmed by phone. Once a price
    /// is set it flows through to <see cref="OrderItem.UnitPriceSnapshot"/>.
    /// </summary>
    public decimal? Price { get; set; }

    /// <summary>How the product is sold — jar, kilo, tray. Shown next to the price.</summary>
    [MaxLength(40)]
    public string? UnitAr { get; set; }

    [MaxLength(40)]
    public string? UnitEn { get; set; }

    public bool InStock { get; set; } = true;

    /// <summary>
    /// Soft delete. Retired products stay in the table so existing orders keep
    /// resolving, but drop off the storefront.
    /// </summary>
    public bool IsActive { get; set; } = true;

    public int SortOrder { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public List<ProductImage> Images { get; set; } = new();
}
