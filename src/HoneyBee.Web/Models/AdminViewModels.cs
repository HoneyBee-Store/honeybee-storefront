using HoneyBee.Web.Services;
using System.ComponentModel.DataAnnotations;

namespace HoneyBee.Web.Models;

public class LoginViewModel
{
    /// <summary>
    /// A username, not an email — the owner signs in with whatever
    /// Admin:UserName is set to. Customers sign in with their phone.
    /// </summary>
    [Required]
    [Display(Name = "Username")]
    public string UserName { get; set; } = "";

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

/// <summary>
/// Backs the admin's email screen. Secrets are write-only: accepted from the
/// form but never sent back to the browser — the Has* flags are all the page
/// needs to know.
/// </summary>
public class EmailSettingsViewModel
{
    [Display(Name = "How to send")]
    public MailProvider Provider { get; set; } = MailProvider.Smtp;

    [Display(Name = "Server")]
    [MaxLength(200)]
    public string? Host { get; set; }

    [Range(1, 65535)]
    public int Port { get; set; } = 587;

    [Display(Name = "Username")]
    [MaxLength(200)]
    public string? User { get; set; }

    [Display(Name = "Send from")]
    [EmailAddress(ErrorMessage = "That does not look like an email address.")]
    [MaxLength(200)]
    public string? From { get; set; }

    [Display(Name = "App password")]
    [MaxLength(200)]
    public string? Password { get; set; }

    [Display(Name = "API key")]
    [MaxLength(400)]
    public string? ApiKey { get; set; }

    public bool IsConfigured { get; set; }
    public bool HasStoredPassword { get; set; }
    public bool HasStoredApiKey { get; set; }
    public string SendsTo { get; set; } = "";

    public bool Saved { get; set; }
    public bool Sent { get; set; }
    public string? Error { get; set; }
}

/// <summary>
/// Backs the passphrase prompt in front of the mail settings.
/// </summary>
public class EmailUnlockViewModel
{
    [Display(Name = "Passphrase")]
    [MaxLength(200)]
    public string? Passphrase { get; set; }

    public string? ReturnUrl { get; set; }

    public bool Failed { get; set; }
    public int AttemptsLeft { get; set; }

    /// <summary>Set while too many wrong attempts are being cooled off.</summary>
    public TimeSpan? LockedFor { get; set; }
}
