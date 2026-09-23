using HoneyBee.Web.Models;
using HoneyBee.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace HoneyBee.Web.Controllers;

/// <summary>
/// A customer's own list of what happened to their orders.
///
/// Signed in only: a notification belongs to an account, and a guest has none.
/// </summary>
[Authorize]
public class NotificationsController : Controller
{
    private readonly NotificationService _notifications;
    private readonly UserManager<AppUser> _users;

    public NotificationsController(NotificationService notifications, UserManager<AppUser> users)
    {
        _notifications = notifications;
        _users = users;
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var userId = await CurrentUserIdAsync();
        if (userId is null) return Challenge();

        var list = await _notifications.ForUserAsync(userId);

        // Opening the list is the act of reading it, so the bell clears here
        // rather than needing a separate button.
        await _notifications.MarkAllReadAsync(userId);

        return View(list);
    }

    /// <summary>
    /// The unread count, for the bell. Polled, so it stays deliberately small
    /// and does nothing but count.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Count()
    {
        var userId = await CurrentUserIdAsync();
        if (userId is null) return Json(new { count = 0 });

        return Json(new { count = await _notifications.UnreadCountAsync(userId) });
    }

    /// <summary>
    /// Looks the account up rather than trusting the claim: the cookie only
    /// proves one was issued, not that the account still exists.
    /// </summary>
    private async Task<string?> CurrentUserIdAsync() => (await _users.GetUserAsync(User))?.Id;
}
