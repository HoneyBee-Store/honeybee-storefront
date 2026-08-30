using System.ComponentModel.DataAnnotations;

namespace HoneyBee.Web.Models;

/// <summary>
/// Somewhere a customer can collect an order. Delivery is deliberately not
/// modelled yet — the shop is pickup-only for now, and adding addresses,
/// zones and fees can wait until someone actually asks for it.
/// </summary>
public class PickupLocation
{
    public int Id { get; set; }

    [Required, MaxLength(80)]
    public string NameAr { get; set; } = "";

    [Required, MaxLength(80)]
    public string NameEn { get; set; } = "";

    /// <summary>Shareable Google Maps link shown to the customer.</summary>
    [MaxLength(400)]
    public string? MapUrl { get; set; }

    public double? Latitude { get; set; }
    public double? Longitude { get; set; }

    public bool IsActive { get; set; } = true;

    public int SortOrder { get; set; }
}
