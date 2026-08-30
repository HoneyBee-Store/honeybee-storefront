using System.ComponentModel.DataAnnotations;

namespace HoneyBee.Web.Models;

public enum OrderStatus
{
    /// <summary>Submitted by the customer, not yet looked at.</summary>
    New = 0,
    /// <summary>Called the customer, price and pickup time agreed.</summary>
    Confirmed = 1,
    /// <summary>Packed and waiting at the pickup point.</summary>
    Ready = 2,
    /// <summary>Collected and paid.</summary>
    Collected = 3,
    Cancelled = 4
}

/// <summary>
/// While the shop has no prices, an order is really an order *request*: the
/// customer picks what they want, and the total is agreed on the phone. The
/// money columns are here from the start so that turning prices on later is a
/// data change rather than a schema migration.
/// </summary>
public class Order
{
    public int Id { get; set; }

    /// <summary>Human-readable reference given to the customer, e.g. "HB-2608-0007".</summary>
    [Required, MaxLength(20)]
    public string OrderNumber { get; set; } = "";

    [Required, MaxLength(120)]
    public string CustomerName { get; set; } = "";

    [Required, MaxLength(30)]
    public string Phone { get; set; } = "";

    public int PickupLocationId { get; set; }
    public PickupLocation? PickupLocation { get; set; }

    public OrderStatus Status { get; set; } = OrderStatus.New;

    [MaxLength(1000)]
    public string? CustomerNotes { get; set; }

    /// <summary>Private — never shown to the customer.</summary>
    [MaxLength(1000)]
    public string? AdminNotes { get; set; }

    /// <summary>Null until prices exist and the order is priced up.</summary>
    public decimal? Total { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public List<OrderItem> Items { get; set; } = new();
}
