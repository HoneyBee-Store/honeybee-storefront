using System.Security.Cryptography;
using HoneyBee.Web.Data;
using HoneyBee.Web.Models;
using HoneyBee.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;

namespace HoneyBee.Web.Controllers;

public class CartController : Controller
{
    private readonly AppDbContext _db;
    private readonly UserManager<AppUser> _users;
    private readonly OrderNotifier _notifier;
    private readonly IStringLocalizer<SharedResource> _l;
    private readonly IDataProtector _pending;

    public CartController(
        AppDbContext db,
        UserManager<AppUser> users,
        OrderNotifier notifier,
        IStringLocalizer<SharedResource> l,
        IDataProtectionProvider protection)
    {
        _db = db;
        _users = users;
        _notifier = notifier;
        _l = l;
        _pending = protection.CreateProtector("HoneyBee.PendingOrder.v1");
    }

    // ---------- basket ----------

    public async Task<IActionResult> Index()
    {
        var cart = await BuildCartAsync();
        cart.PreviousOrders = await MyOrdersAsync();
        return View(cart);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Add(int productId, decimal sizeKg, int quantity = 1, string? returnUrl = null)
    {
        var product = await _db.Products
            .FirstOrDefaultAsync(p => p.Id == productId && p.IsActive);

        if (product is null) return NotFound();

        // Checked here, not only in the view: a stale page could still post an
        // item that sold out since it was rendered.
        if (!product.InStock)
        {
            TempData["CartMessage"] = _l["Sorry, that product is out of stock."].Value;
            return LocalRedirect(Url.IsLocalUrl(returnUrl) ? returnUrl! : "/");
        }

        if (sizeKg is not (0.5m or 1m)) sizeKg = 1m;
        quantity = Math.Clamp(quantity, 1, 999);

        var cart = HttpContext.Session.GetCart();
        cart.Add(product.Id, sizeKg, quantity);
        HttpContext.Session.SaveCart(cart);

        TempData["CartMessage"] = _l["Added to your request."].Value;
        return LocalRedirect(Url.IsLocalUrl(returnUrl) ? returnUrl! : Url.Action(nameof(Index))!);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Update(int productId, decimal sizeKg, int quantity)
    {
        var cart = HttpContext.Session.GetCart();
        cart.SetQuantity(productId, sizeKg, quantity);
        HttpContext.Session.SaveCart(cart);
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Remove(int productId, decimal sizeKg)
    {
        var cart = HttpContext.Session.GetCart();
        cart.Remove(productId, sizeKg);
        HttpContext.Session.SaveCart(cart);
        return RedirectToAction(nameof(Index));
    }

    // ---------- checkout ----------

    // Guest checkout: requiring an account first cost more orders than it was
    // worth. Signed-in customers still get their details filled in.
    [HttpGet]
    public async Task<IActionResult> Checkout()
    {
        var cart = await BuildCartAsync();
        if (cart.IsEmpty) return RedirectToAction(nameof(Index));

        var user = User.Identity?.IsAuthenticated == true
            ? await _users.GetUserAsync(User)
            : null;

        return View(new CheckoutViewModel
        {
            // Pre-filled from the account, but editable — someone may be
            // collecting on behalf of a relative.
            CustomerName = user?.FullName ?? "",
            Phone = user?.PhoneNumber ?? "",
            Cart = cart,
            PickupLocations = await ActiveLocationsAsync()
        });
    }

    /// <summary>
    /// Placing an order, in two stages.
    ///
    /// The first press saves the order as AwaitingPayment and tells the
    /// customer to wait while the CliQ transfer is checked. Every press after
    /// that asks the same question again — "has it been approved yet?" — and
    /// only the press that finds it approved finishes the job. That is why
    /// nothing here reloads the page: the customer stays put and presses again,
    /// and the moment the owner approves, the next press goes through.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Checkout(CheckoutViewModel model)
    {
        // Asked before anything else, and before the cart is even looked at: a
        // second press must never create a second order, and by this point the
        // order already carries its own copy of everything the cart held.
        var pending = await FindPendingOrderAsync();
        if (pending is not null) return await ResolveAsync(pending);

        var cart = await BuildCartAsync();
        if (cart.IsEmpty) return RedirectToAction(nameof(Index));

        if (!PhoneNumbers.LooksValid(model.Phone))
        {
            ModelState.AddModelError(nameof(model.Phone),
                _l["Enter a Jordanian mobile number, e.g. 0790000000."]);
        }

        if (!await _db.PickupLocations.AnyAsync(l => l.Id == model.PickupLocationId && l.IsActive))
        {
            ModelState.AddModelError(nameof(model.PickupLocationId),
                _l["Please choose where to collect your order."]);
        }

        if (!ModelState.IsValid)
        {
            if (WantsJson) return ValidationJson();

            model.Cart = cart;
            model.PickupLocations = await ActiveLocationsAsync();
            return View(model);
        }

        var order = new Order
        {
            OrderNumber = await NextOrderNumberAsync(),
            CustomerName = model.CustomerName.Trim(),
            Phone = PhoneNumbers.Normalise(model.Phone),
            PickupLocationId = model.PickupLocationId,
            CustomerNotes = model.CustomerNotes?.Trim(),
            // Held until the transfer is confirmed, not New. Nothing about this
            // order reaches the customer as "placed" until someone says so.
            Status = OrderStatus.AwaitingPayment,
            // Stamped so this order is still theirs after the session cookie is
            // gone. Null for guests, which is why the phone is matched too.
            //
            // Looked up rather than read straight off the claim: the claim only
            // proves a cookie was issued, not that the account still exists. The
            // admin can delete a customer, and their next order would then fail
            // on the foreign key with a 500 in the middle of checkout.
            UserId = (await CurrentUserAsync())?.Id,
            Total = cart.Total
        };

        foreach (var line in cart.Lines)
        {
            order.Items.Add(new OrderItem
            {
                ProductId = line.Product.Id,
                // Copied, not joined: renaming or repricing a product later must
                // not rewrite what this order says.
                NameSnapshot = line.Product.NameAr,
                SizeKg = line.SizeKg,
                UnitPriceSnapshot = line.UnitPrice,
                Quantity = line.Quantity
            });
        }

        _db.Orders.Add(order);
        await _db.SaveChangesAsync();

        // Saved first, notified second — a mail failure must never lose an order.
        // The email is queued rather than awaited: the body is built here while
        // the order is loaded, but the customer is not held on this button while
        // a mail server is contacted.
        await _db.Entry(order).Reference(o => o.PickupLocation).LoadAsync();
        await _notifier.QueueOrderEmailAsync(order);

        // The cart is deliberately left alone until the order is approved. If it
        // were emptied here, a customer who reloaded while waiting would find an
        // empty basket and no obvious way back to their own order.
        RememberOrder(order.OrderNumber);
        SetPendingOrder(order.OrderNumber);

        return await ResolveAsync(order);
    }

    /// <summary>
    /// Answers the one question a press of the button asks: may this order
    /// proceed? Used for the first press and for every press after it, so the
    /// two cases cannot drift apart.
    /// </summary>
    private async Task<IActionResult> ResolveAsync(Order order)
    {
        if (order.IsAwaitingPayment)
        {
            return Respond("waiting", order,
                _l["Please wait until your transfer arrives — it will be approved in a moment."]);
        }

        if (order.Status == OrderStatus.Cancelled)
        {
            // Told plainly rather than left pressing a button forever.
            ClearPendingOrder();
            return Respond("cancelled", order,
                _l["This request was cancelled. Please contact us if that is unexpected."]);
        }

        // Approved. Only now is the basket emptied and the customer sent on.
        HttpContext.Session.SaveCart(new Cart());
        ClearPendingOrder();
        TempData["JustOrdered"] = order.OrderNumber;

        if (order.PickupLocation is null)
        {
            await _db.Entry(order).Reference(o => o.PickupLocation).LoadAsync();
        }

        // Straight to WhatsApp rather than via a confirmation page with a button
        // on it — that page was one extra click for something the customer had
        // already asked for. The order is saved either way, and Confirmation is
        // still reachable by order number.
        return Respond("approved", order, null, _notifier.BuildWhatsAppLink(order));
    }

    /// <summary>
    /// The same outcome in both dialects: JSON for the button's own request, and
    /// an ordinary redirect for anyone whose JavaScript did not run. Without the
    /// second, the checkout button would simply be dead with scripts blocked.
    /// </summary>
    private IActionResult Respond(string state, Order order, LocalizedString? message, string? url = null)
    {
        if (WantsJson)
        {
            return Json(new { state, orderNumber = order.OrderNumber, message = message?.Value, url });
        }

        if (state == "approved") return Redirect(url!);

        TempData["CheckoutState"] = state;
        TempData["CheckoutMessage"] = message?.Value;
        TempData["PendingOrder"] = order.OrderNumber;
        return RedirectToAction(nameof(Checkout));
    }

    /// <summary>
    /// True when this is the checkout button's own request rather than a browser
    /// navigating. Set explicitly by the script; nothing else sends it.
    /// </summary>
    private bool WantsJson => Request.Headers["X-Requested-With"] == "fetch";

    private IActionResult ValidationJson()
    {
        var errors = ModelState
            .Where(kv => kv.Value?.Errors.Count > 0)
            .ToDictionary(
                kv => kv.Key,
                kv => kv.Value!.Errors.Select(e => e.ErrorMessage).ToArray());

        return Json(new { state = "invalid", errors });
    }

    /// <summary>
    /// The order this customer is already waiting on, if there is one.
    ///
    /// Session first, because it covers guests as well. Then the database by
    /// account, because the session is held in memory and a free host recycles
    /// the app pool often — without that fallback, one recycle mid-wait would
    /// have a signed-in customer create a duplicate order on their next press.
    /// </summary>
    private async Task<Order?> FindPendingOrderAsync()
    {
        var remembered = PendingOrderNumber();

        if (!string.IsNullOrEmpty(remembered))
        {
            var order = await _db.Orders
                .Include(o => o.Items)
                .Include(o => o.PickupLocation)
                .FirstOrDefaultAsync(o => o.OrderNumber == remembered);

            // Whatever its status. Narrowing this to "still waiting" looks
            // safer and is in fact the bug that stops the flow working: the
            // press that matters is the one made *after* approval, and that
            // press would find nothing and place a second order for the same
            // money. ResolveAsync clears the cookie once it hands over, which
            // is what stops a finished order being resolved twice.
            if (order is not null) return order;

            ClearPendingOrder();
        }

        var userId = (await CurrentUserAsync())?.Id;
        if (userId is null) return null;

        return await _db.Orders
            .Include(o => o.Items)
            .Include(o => o.PickupLocation)
            .Where(o => o.UserId == userId && o.Status == OrderStatus.AwaitingPayment)
            .OrderByDescending(o => o.CreatedAt)
            .FirstOrDefaultAsync();
    }

    /// <summary>
    /// Only the visitor who placed the order (or the owner) may see it. Order
    /// numbers run in sequence, so without this check anyone could count up
    /// from HB-2608-0001 and read every customer's name and phone number.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Confirmation(string orderNumber)
    {
        var order = await _db.Orders
            .Include(o => o.Items)
            .Include(o => o.PickupLocation)
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.OrderNumber == orderNumber);

        if (order is null) return NotFound();
        if (!await MayViewAsync(order)) return NotFound();

        ViewData["WhatsAppLink"] = _notifier.BuildWhatsAppLink(order);
        return View(order);
    }

    // ---------- helpers ----------

    private async Task<List<PickupLocation>> ActiveLocationsAsync() =>
        await _db.PickupLocations
            .Where(l => l.IsActive)
            .OrderBy(l => l.SortOrder)
            .AsNoTracking()
            .ToListAsync();

    /// <summary>
    /// Rebuilds the basket against the database on every request, so a product
    /// that was retired or repriced mid-visit is reflected rather than cached.
    /// </summary>
    private async Task<CartViewModel> BuildCartAsync()
    {
        var cart = HttpContext.Session.GetCart();
        if (cart.Lines.Count == 0) return new CartViewModel();

        var ids = cart.Lines.Select(l => l.ProductId).Distinct().ToList();
        var products = await _db.Products
            .Where(p => ids.Contains(p.Id) && p.IsActive)
            .AsNoTracking()
            .ToDictionaryAsync(p => p.Id);

        var model = new CartViewModel();
        var dropped = false;

        foreach (var line in cart.Lines.ToList())
        {
            if (!products.TryGetValue(line.ProductId, out var product))
            {
                cart.Lines.Remove(line);   // retired since it was added
                dropped = true;
                continue;
            }

            model.Lines.Add(new CartLineViewModel
            {
                Product = product,
                SizeKg = line.SizeKg,
                Quantity = line.Quantity
            });
        }

        if (dropped) HttpContext.Session.SaveCart(cart);

        return model;
    }

    private const string MyOrdersKey = "myOrders";
    private const string PendingOrderCookie = "hb_pending";

    /// <summary>
    /// Remembers which order this visitor is waiting on.
    ///
    /// A cookie rather than the session, because the session lives in memory and
    /// the host recycles the app pool freely — a recycle mid-wait would lose the
    /// marker and have a guest create a second order for the same money.
    ///
    /// Signed, because it is now something the visitor holds. An unsigned order
    /// number could be edited to name someone else's order, and the approved
    /// branch would then hand over that customer's name and phone in a WhatsApp
    /// link. Data protection makes the value unforgeable.
    /// </summary>
    private void SetPendingOrder(string orderNumber) =>
        Response.Cookies.Append(PendingOrderCookie, _pending.Protect(orderNumber), new CookieOptions
        {
            HttpOnly = true,
            IsEssential = true,
            SameSite = SameSiteMode.Lax,
            Secure = Request.IsHttps,
            Expires = DateTimeOffset.UtcNow.AddDays(7)
        });

    private void ClearPendingOrder() => Response.Cookies.Delete(PendingOrderCookie);

    private string? PendingOrderNumber()
    {
        var value = Request.Cookies[PendingOrderCookie];
        if (string.IsNullOrEmpty(value)) return null;

        try
        {
            return _pending.Unprotect(value);
        }
        catch (CryptographicException)
        {
            // Tampered with, or signed by keys that have since been replaced.
            // Either way it is meaningless now; drop it rather than 500.
            ClearPendingOrder();
            return null;
        }
    }

    private void RememberOrder(string orderNumber)
    {
        var existing = HttpContext.Session.GetString(MyOrdersKey) ?? "";
        HttpContext.Session.SetString(MyOrdersKey, $"{existing}{orderNumber},");
    }

    private bool PlacedInThisSession(string orderNumber) =>
        (HttpContext.Session.GetString(MyOrdersKey) ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Contains(orderNumber);

    /// <summary>
    /// The signed-in account, or null — including when the cookie names an
    /// account that no longer exists.
    /// </summary>
    private async Task<AppUser?> CurrentUserAsync() =>
        User.Identity?.IsAuthenticated == true ? await _users.GetUserAsync(User) : null;

    /// <summary>
    /// Whether this visitor is allowed to read this order.
    ///
    /// Order numbers run in sequence, so without a check anyone could count up
    /// from HB-2609-0001 and read every customer's name and phone number.
    ///
    /// Three ways to qualify, in widening order of durability: the session that
    /// placed it, the account it is stamped with, and the phone number on the
    /// signed-in account. The last one is what rescues orders placed as a guest,
    /// or before this column existed — the phone is how customers sign in, so
    /// matching on it grants nothing they could not already prove.
    /// </summary>
    private async Task<bool> MayViewAsync(Order order)
    {
        if (PlacedInThisSession(order.OrderNumber)) return true;
        if (User.IsInRole(Roles.Admin)) return true;

        var user = await CurrentUserAsync();
        if (user is null) return false;
        if (order.UserId == user.Id) return true;

        return !string.IsNullOrEmpty(user.PhoneNumber)
               && order.Phone == PhoneNumbers.Normalise(user.PhoneNumber);
    }

    /// <summary>
    /// This customer's own past requests, newest first.
    ///
    /// Signed-in customers are matched on their account and on their phone
    /// number, which picks up anything they ordered as a guest beforehand.
    /// Guests get only what this session remembers, which is the best that can
    /// be done for someone the site cannot identify.
    /// </summary>
    private async Task<List<Order>> MyOrdersAsync()
    {
        var user = await CurrentUserAsync();

        var query = _db.Orders
            .Include(o => o.Items)
            .Include(o => o.PickupLocation)
            .AsNoTracking();

        if (user is not null)
        {
            var phone = PhoneNumbers.Normalise(user.PhoneNumber ?? "");

            return await query
                .Where(o => o.UserId == user.Id || (phone != "" && o.Phone == phone))
                .OrderByDescending(o => o.CreatedAt)
                .Take(20)
                .ToListAsync();
        }

        var numbers = (HttpContext.Session.GetString(MyOrdersKey) ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Distinct()
            .ToList();

        if (numbers.Count == 0) return new List<Order>();

        return await query
            .Where(o => numbers.Contains(o.OrderNumber))
            .OrderByDescending(o => o.CreatedAt)
            .ToListAsync();
    }

    /// <summary>
    /// Next reference for this month, taken from the highest number already
    /// used rather than from a count. Counting breaks as soon as an order is
    /// deleted: the count drops, the next order reuses a live number, and the
    /// unique index rejects it mid-checkout.
    ///
    /// The suffix is zero-padded to a fixed width, so ordering the strings
    /// orders the numbers.
    /// </summary>
    private async Task<string> NextOrderNumberAsync()
    {
        var prefix = $"HB-{DateTime.UtcNow:yyMM}-";

        var highest = await _db.Orders
            .Where(o => o.OrderNumber.StartsWith(prefix))
            .OrderByDescending(o => o.OrderNumber)
            .Select(o => o.OrderNumber)
            .FirstOrDefaultAsync();

        var next = 1;
        if (highest is not null && int.TryParse(highest[prefix.Length..], out var last))
        {
            next = last + 1;
        }

        return $"{prefix}{next:D4}";
    }
}
