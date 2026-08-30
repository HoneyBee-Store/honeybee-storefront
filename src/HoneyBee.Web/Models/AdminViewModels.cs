using System.ComponentModel.DataAnnotations;

namespace HoneyBee.Web.Models;

public class LoginViewModel
{
    [Required, EmailAddress]
    public string Email { get; set; } = "";

    [Required, DataType(DataType.Password)]
    public string Password { get; set; } = "";

    public string? ReturnUrl { get; set; }
}

public class ProductEditViewModel
{
    public int Id { get; set; }

    [Required, MaxLength(80)]
    [Display(Name = "Slug (URL)")]
    [RegularExpression("^[a-z0-9]+(-[a-z0-9]+)*$",
        ErrorMessage = "Lowercase letters, numbers and hyphens only — e.g. sidr-honey.")]
    public string Slug { get; set; } = "";

    [Required, MaxLength(120)]
    [Display(Name = "Name (Arabic)")]
    public string NameAr { get; set; } = "";

    [Required, MaxLength(120)]
    [Display(Name = "Name (English)")]
    public string NameEn { get; set; } = "";

    [MaxLength(2000)]
    [Display(Name = "Description (Arabic)")]
    public string? DescriptionAr { get; set; }

    [MaxLength(2000)]
    [Display(Name = "Description (English)")]
    public string? DescriptionEn { get; set; }

    [Range(0, 99999)]
    [Display(Name = "Price (JOD) — leave blank for 'price on request'")]
    public decimal? Price { get; set; }

    [MaxLength(40)]
    [Display(Name = "Unit (Arabic)")]
    public string? UnitAr { get; set; }

    [MaxLength(40)]
    [Display(Name = "Unit (English)")]
    public string? UnitEn { get; set; }

    [Display(Name = "In stock")]
    public bool InStock { get; set; } = true;

    [Display(Name = "Visible on the site")]
    public bool IsActive { get; set; } = true;

    [Display(Name = "Sort order")]
    public int SortOrder { get; set; }

    /// <summary>Vertical crop point. Existing photos were measured; 50 is centre.</summary>
    [Range(0, 100)]
    [Display(Name = "Image focal point (% from top)")]
    public int FocalY { get; set; } = 50;

    public string? CurrentImagePath { get; set; }
}
