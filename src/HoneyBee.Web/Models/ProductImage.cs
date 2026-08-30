using System.ComponentModel.DataAnnotations;

namespace HoneyBee.Web.Models;

public class ProductImage
{
    public int Id { get; set; }

    public int ProductId { get; set; }
    public Product? Product { get; set; }

    /// <summary>Path relative to wwwroot, e.g. "img/products/honey-sidr.jpg".</summary>
    [Required, MaxLength(260)]
    public string Path { get; set; } = "";

    [MaxLength(200)]
    public string? AltAr { get; set; }

    [MaxLength(200)]
    public string? AltEn { get; set; }

    /// <summary>
    /// Vertical focal point as a percentage, fed straight into CSS
    /// object-position. These photos are tall phone shots where the jar sits
    /// high in frame, so a default centre crop lands on the floor instead of
    /// the product. Measured per photo rather than guessed.
    /// </summary>
    [Range(0, 100)]
    public int FocalY { get; set; } = 50;

    public bool IsPrimary { get; set; }

    public int SortOrder { get; set; }
}
