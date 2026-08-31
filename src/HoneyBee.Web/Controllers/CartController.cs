using HoneyBee.Web.Data;
using HoneyBee.Web.Models;
using HoneyBee.Web.Services;
using Microsoft.AspNetCore.Authorization;
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

    public CartController(
        AppDbContext db,
        UserManager<AppUser> users,
        OrderNotifier notifier,
        IStringLocalizer<SharedResource> l)
    {
        _db = db;
        _users = users;
        _notifier = notifier;
        _l = l;
    }

    // ---------- basket ----------

    public async Task<IActionResult> Index() => View(await BuildCartAsync());

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

    [Authorize]
    [HttpGet]
    public async Task<IActionResult> Checkout()
    {
        var cart = await BuildCartAsync();
        if (cart.IsEmpty) return RedirectToAction(nameof(Index));

        var user = await _users.GetUserAsync(User);

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

    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Checkout(CheckoutViewModel model)
    {
        var cart = await BuildCartAsync();
        if (cart.IsEmpty) return RedirectToAction(nameof(Index));

        if (!await _db.PickupLocations.AnyAsync(l => l.Id == model.PickupLocationId && l.IsActive))
        {
            ModelState.AddModelError(nameof(model.PickupLocationId),
                _l["Please choose where to collect your order."]);
        }

        if (!ModelState.IsValid)
        {
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
            Status = OrderStatus.New,
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
        await _db.Entry(order).Reference(o => o.PickupLocation).LoadAsync();
        await _notifier.TryEmailAsync(order);

        HttpContext.Session.SaveCart(new Cart());

        // Straight to WhatsApp rather than via a confirmation page with a button
        // on it — that page was one extra click for something the customer had
        // already asked for. The order is saved either way, and Confirmation is
        // still reachable by order number.
        TempData["JustOrdered"] = order.OrderNumber;
        return Redirect(_notifier.BuildWhatsAppLink(order));
    }

    [Authorize]
    [HttpGet]
    public async Task<IActionResult> Confirmation(string orderNumber)
    {
        var order = await _db.Orders
            .Include(o => o.Items)
            .Include(o => o.PickupLocation)
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.OrderNumber == orderNumber);

        if (order is null) return NotFound();

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

    private async Task<string> NextOrderNumberAsync()
    {
        var prefix = $"HB-{DateTime.UtcNow:yyMM}-";
        var used = await _db.Orders
            .Where(o => o.OrderNumber.StartsWith(prefix))
            .CountAsync();

        return $"{prefix}{used + 1:D4}";
    }
}
