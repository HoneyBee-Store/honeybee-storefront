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
    /// Placed, and waiting for the shop to let it through. What the shop is
    /// waiting *for* depends on how the customer chose to pay: a CliQ transfer
    /// to arrive, or simply a decision on the request for cash on collection.
    ///
    /// Numbered after Cancelled rather than before New because the values are
    /// stored as integers — inserting it in the middle would renumber every
    /// status and silently rewrite the meaning of every existing order.
    /// </summary>
    AwaitingApproval = 5
}

/// <summary>How the customer intends to pay.</summary>
public enum PaymentMethod
{
    /// <summary>
    /// Transferred to the shop's CliQ alias before collection. The shop has to
    /// see the money land before releasing the order.
    /// </summary>
    Cliq = 0,

    /// <summary>
    /// Cash, handed over at the pickup point. Nothing to verify in advance, so
    /// approving is only the shop agreeing to the request.
    /// </summary>
    CashOnPickup = 1
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
    /// Defaults to CliQ because that is the only way orders could be paid when
    /// this column was added — every order already in the table was one.
    /// </summary>
    public PaymentMethod PaymentMethod { get; set; } = PaymentMethod.Cliq;

    /// <summary>True when the shop is waiting on money, not just on a decision.</summary>
    public bool NeedsTransfer => PaymentMethod == PaymentMethod.Cliq;

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

    /// <summary>Waiting on the shop to let this through.</summary>
    public bool IsAwaitingApproval => Status == OrderStatus.AwaitingApproval;

    /// <summary>
    /// Cleared to proceed. Anything that is not "waiting" and not "cancelled"
    /// means someone has looked at this order and let it through.
    /// </summary>
    public bool IsApproved =>
        Status is not (OrderStatus.AwaitingApproval or OrderStatus.Cancelled);

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
