using System.ComponentModel.DataAnnotations;

namespace HoneyBee.Web.Models;

public class OrderItem
{
    public int Id { get; set; }

    public int OrderId { get; set; }
    public Order? Order { get; set; }

    /// <summary>
    /// Kept for reporting ("how much Sidr did we sell?"). Never rely on it to
    /// render a past order — read the snapshot columns instead.
    /// </summary>
    public int? ProductId { get; set; }
    public Product? Product { get; set; }

    /// <summary>
    /// The name as it was when ordered. Copied rather than joined so that
    /// renaming or retiring a product doesn't silently rewrite history.
    /// </summary>
    [Required, MaxLength(120)]
    public string NameSnapshot { get; set; } = "";

    /// <summary>
    /// Jar size in kilograms — 1 or 0.5. Honey is priced per kilo, so the size
    /// is what turns a rate into a line price.
    /// </summary>
    public decimal SizeKg { get; set; } = 1m;

    /// <summary>
    /// Price of ONE jar of this size, captured at checkout — not the per-kg
    /// rate. Storing what was actually charged means raising a price next month
    /// cannot change what last month's orders say they cost.
    /// </summary>
    public decimal? UnitPriceSnapshot { get; set; }

    [Range(1, 999)]
    public int Quantity { get; set; } = 1;

    public decimal? LineTotal => UnitPriceSnapshot * Quantity;
}
