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
    /// The price charged, captured at checkout. Null while the shop is running
    /// without prices. Same reasoning as <see cref="NameSnapshot"/>: raising a
    /// price next month must not change what last month's orders say.
    /// </summary>
    public decimal? UnitPriceSnapshot { get; set; }

    [Range(1, 999)]
    public int Quantity { get; set; } = 1;
}
