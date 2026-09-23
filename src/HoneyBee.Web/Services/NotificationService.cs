using HoneyBee.Web.Data;
using HoneyBee.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace HoneyBee.Web.Services;

/// <summary>
/// Telling a customer what happened to their order.
///
/// Every write goes through here rather than being scattered across the admin
/// actions, so there is one place that decides who a notification belongs to
/// and one place that decides what counts as worth telling someone about.
/// </summary>
public class NotificationService
{
    private readonly AppDbContext _db;

    public NotificationService(AppDbContext db) => _db = db;

    /// <summary>
    /// Records a change against the account that placed the order, if there is
    /// one. Saving is left to the caller so the notification lands in the same
    /// transaction as the status change it describes — a customer should never
    /// be told about something that then failed to save.
    /// </summary>
    public async Task RecordAsync(Order order, NotificationKind kind)
    {
        var userId = await ResolveRecipientAsync(order);
        if (userId is null) return;

        _db.Notifications.Add(new Notification
        {
            UserId = userId,
            OrderId = order.Id,
            // Copied, not joined: the number must survive the order being removed.
            OrderNumber = order.OrderNumber,
            Kind = kind
        });
    }

    /// <summary>
    /// Maps an order status onto something worth telling the customer, or null
    /// when it is bookkeeping they do not need to hear about.
    /// </summary>
    public static NotificationKind? KindFor(OrderStatus status) => status switch
    {
        OrderStatus.New => NotificationKind.Approved,
        OrderStatus.Confirmed => NotificationKind.Confirmed,
        OrderStatus.Ready => NotificationKind.Ready,
        OrderStatus.Collected => NotificationKind.Collected,
        OrderStatus.Cancelled => NotificationKind.Cancelled,
        OrderStatus.OnHold => NotificationKind.OnHold,
        // Nothing has been decided yet; the customer is already being told to
        // wait by the checkout page itself.
        OrderStatus.AwaitingApproval => null,
        _ => null
    };

    public async Task<List<Notification>> ForUserAsync(string userId, int take = 20) =>
        await _db.Notifications
            .Where(n => n.UserId == userId)
            .OrderByDescending(n => n.CreatedAt)
            .ThenByDescending(n => n.Id)
            .Take(take)
            .AsNoTracking()
            .ToListAsync();

    public async Task<int> UnreadCountAsync(string userId) =>
        await _db.Notifications.CountAsync(n => n.UserId == userId && n.ReadAt == null);

    /// <summary>Marks everything this customer has as read.</summary>
    public async Task<int> MarkAllReadAsync(string userId)
    {
        var now = DateTime.UtcNow;

        return await _db.Notifications
            .Where(n => n.UserId == userId && n.ReadAt == null)
            .ExecuteUpdateAsync(n => n.SetProperty(x => x.ReadAt, now));
    }

    /// <summary>
    /// Which account, if any, should hear about this order.
    ///
    /// The stamped account first. Failing that, an account with the same phone
    /// number: customers can order as a guest and register afterwards, and the
    /// phone is how they sign in, so matching on it hands the notification to
    /// the person who actually placed the order. Refuses to guess when two
    /// accounts share a number.
    /// </summary>
    private async Task<string?> ResolveRecipientAsync(Order order)
    {
        if (!string.IsNullOrEmpty(order.UserId)) return order.UserId;
        if (string.IsNullOrWhiteSpace(order.Phone)) return null;

        var matches = await _db.Users
            .Where(u => u.PhoneNumber == order.Phone)
            .Select(u => u.Id)
            .Take(2)
            .ToListAsync();

        return matches.Count == 1 ? matches[0] : null;
    }
}
