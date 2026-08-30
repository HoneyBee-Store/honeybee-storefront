using System.Diagnostics;
using HoneyBee.Web.Data;
using HoneyBee.Web.Models;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HoneyBee.Web.Controllers;

public class HomeController : Controller
{
    private readonly AppDbContext _db;

    public HomeController(AppDbContext db) => _db = db;

    public async Task<IActionResult> Index()
    {
        var model = new StorefrontViewModel
        {
            Products = await _db.Products
                .Where(p => p.IsActive)
                .Include(p => p.Images.Where(i => i.IsPrimary))
                .OrderBy(p => p.SortOrder)
                .AsNoTracking()
                .ToListAsync(),

            PickupLocations = await _db.PickupLocations
                .Where(l => l.IsActive)
                .OrderBy(l => l.SortOrder)
                .AsNoTracking()
                .ToListAsync()
        };

        return View(model);
    }

    [Route("product/{slug}")]
    public async Task<IActionResult> Product(string slug)
    {
        var product = await _db.Products
            .Include(p => p.Images.OrderBy(i => i.SortOrder))
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Slug == slug && p.IsActive);

        if (product is null) return NotFound();

        return View(product);
    }

    /// <summary>
    /// Stores the language choice in the culture cookie and returns the visitor
    /// to where they were. Only the two supported cultures are accepted, and
    /// returnUrl is checked with IsLocalUrl so this can't be used as an open
    /// redirect.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult SetLanguage(string culture, string? returnUrl)
    {
        if (culture is "ar" or "en")
        {
            Response.Cookies.Append(
                CookieRequestCultureProvider.DefaultCookieName,
                CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(culture)),
                new CookieOptions
                {
                    Expires = DateTimeOffset.UtcNow.AddYears(1),
                    IsEssential = true,
                    SameSite = SameSiteMode.Lax
                });
        }

        return LocalRedirect(Url.IsLocalUrl(returnUrl) ? returnUrl! : "/");
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
        => View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
}
