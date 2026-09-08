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
    Cancelled = 4,
    /// <summary>
    /// Placed, but the shop has not yet seen the CliQ transfer arrive. The
    /// customer cannot finish until someone confirms the money is in.
    ///
    /// Numbered after Cancelled rather than before New because the values are
    /// stored as integers — inserting it in the middle would renumber every
    /// status and silently rewrite the meaning of every existing order.
    /// </summary>
    AwaitingPayment = 5
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

    /// <summary>
    /// The account that placed this, when the customer was signed in.
    ///
    /// Nullable on purpose: guests can order without registering, and every
    /// order placed before this column existed has nobody to point at. It is
    /// what lets someone see their own past requests after the session cookie
    /// is gone — which, on a free host that recycles the app pool, is often.
    /// </summary>
    [MaxLength(450)]
    public string? UserId { get; set; }
    public AppUser? User { get; set; }

    /// <summary>Waiting on the shop to confirm the transfer arrived.</summary>
    public bool IsAwaitingPayment => Status == OrderStatus.AwaitingPayment;

    /// <summary>
    /// Cleared to proceed. Anything that is not "waiting" and not "cancelled"
    /// means someone has looked at this order and let it through.
    /// </summary>
    public bool IsPaymentApproved =>
        Status is not (OrderStatus.AwaitingPayment or OrderStatus.Cancelled);

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
