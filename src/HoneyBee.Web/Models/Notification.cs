using System.ComponentModel.DataAnnotations;

namespace HoneyBee.Web.Models;

/// <summary>
/// What happened to an order, from the customer's point of view.
///
/// Stored instead of a sentence, because the shop is bilingual: the wording is
/// looked up when the notification is shown, so the same row reads correctly in
/// Arabic or English depending on who is looking and when.
/// </summary>
public enum NotificationKind
{
    /// <summary>Let through — the transfer landed, or the request was accepted.</summary>
    Approved = 0,

    /// <summary>Turned down.</summary>
    Cancelled = 1,

    /// <summary>Paused by the shop, with nothing expected of the customer yet.</summary>
    OnHold = 2,

    /// <summary>Price and collection time agreed.</summary>
    Confirmed = 3,

    /// <summary>Packed and waiting at the pickup point.</summary>
    Ready = 4,

    /// <summary>Handed over.</summary>
    Collected = 5
}

/// <summary>
/// One line in a customer's notification list.
///
/// Rows are per account, so only someone who registered can receive them — a
/// guest order has no inbox to deliver to. The order number is copied in rather
/// than read through the relation, so the notification still reads correctly if
/// the order is ever removed.
/// </summary>
public class Notification
{
    public int Id { get; set; }

    [Required, MaxLength(450)]
    public string UserId { get; set; } = "";
    public AppUser? User { get; set; }

    public int? OrderId { get; set; }
    public Order? Order { get; set; }

    [Required, MaxLength(20)]
    public string OrderNumber { get; set; } = "";

    public NotificationKind Kind { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Null until the customer has looked at their list.</summary>
    public DateTime? ReadAt { get; set; }

    public bool IsUnread => ReadAt is null;
}
