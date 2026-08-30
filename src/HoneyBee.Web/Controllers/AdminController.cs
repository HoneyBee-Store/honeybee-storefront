using HoneyBee.Web.Data;
using HoneyBee.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HoneyBee.Web.Controllers;

[Authorize]
public class AdminController : Controller
{
    private const long MaxImageBytes = 8 * 1024 * 1024;

    // Allowlist rather than a blocklist, and checked against the real extension
    // rather than whatever the browser claims the content type is.
    private static readonly string[] AllowedImageExtensions = [".jpg", ".jpeg", ".png", ".webp"];

    private readonly AppDbContext _db;
    private readonly SignInManager<IdentityUser> _signIn;
    private readonly IWebHostEnvironment _env;

    public AdminController(AppDbContext db, SignInManager<IdentityUser> signIn, IWebHostEnvironment env)
    {
        _db = db;
        _signIn = signIn;
        _env = env;
    }

    // ---------- auth ----------

    [AllowAnonymous]
    [HttpGet]
    public IActionResult Login(string? returnUrl) => View(new LoginViewModel { ReturnUrl = returnUrl });

    [AllowAnonymous]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model)
    {
        if (!ModelState.IsValid) return View(model);

        // lockoutOnFailure guards against someone grinding through passwords;
        // the limit is set in Program.cs.
        var result = await _signIn.PasswordSignInAsync(
            model.Email, model.Password, isPersistent: true, lockoutOnFailure: true);

        if (result.Succeeded)
        {
            return LocalRedirect(Url.IsLocalUrl(model.ReturnUrl)
                ? model.ReturnUrl!
                : Url.Action(nameof(Products))!);
        }

        // Deliberately vague: saying "no such account" tells an attacker which
        // addresses are real.
        ModelState.AddModelError(string.Empty,
            result.IsLockedOut
                ? "Too many attempts. Try again later."
                : "Incorrect email or password.");

        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await _signIn.SignOutAsync();
        return RedirectToAction(nameof(HomeController.Index), "Home");
    }

    // ---------- products ----------

    public async Task<IActionResult> Products()
    {
        var products = await _db.Products
            .Include(p => p.Images.Where(i => i.IsPrimary))
            .OrderBy(p => p.SortOrder)
            .AsNoTracking()
            .ToListAsync();

        return View(products);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleStock(int id)
    {
        var product = await _db.Products.FindAsync(id);
        if (product is null) return NotFound();

        product.InStock = !product.InStock;
        product.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        TempData["Message"] = $"{product.NameEn} is now {(product.InStock ? "in stock" : "out of stock")}.";
        return RedirectToAction(nameof(Products));
    }

    [HttpGet]
    public async Task<IActionResult> EditProduct(int? id)
    {
        if (id is null)
        {
            var nextOrder = await _db.Products.AnyAsync()
                ? await _db.Products.MaxAsync(p => p.SortOrder) + 1
                : 1;
            return View(new ProductEditViewModel { SortOrder = nextOrder });
        }

        var product = await _db.Products
            .Include(p => p.Images.Where(i => i.IsPrimary))
            .FirstOrDefaultAsync(p => p.Id == id);

        if (product is null) return NotFound();

        var image = product.Images.FirstOrDefault();

        return View(new ProductEditViewModel
        {
            Id = product.Id,
            Slug = product.Slug,
            NameAr = product.NameAr,
            NameEn = product.NameEn,
            DescriptionAr = product.DescriptionAr,
            DescriptionEn = product.DescriptionEn,
            Price = product.Price,
            UnitAr = product.UnitAr,
            UnitEn = product.UnitEn,
            InStock = product.InStock,
            IsActive = product.IsActive,
            SortOrder = product.SortOrder,
            FocalY = image?.FocalY ?? 50,
            CurrentImagePath = image?.Path
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(MaxImageBytes + 1024 * 1024)]
    public async Task<IActionResult> EditProduct(ProductEditViewModel model, IFormFile? image)
    {
        var slugTaken = await _db.Products
            .AnyAsync(p => p.Slug == model.Slug && p.Id != model.Id);

        if (slugTaken)
        {
            ModelState.AddModelError(nameof(model.Slug), "Another product already uses this slug.");
        }

        string? uploadedPath = null;
        if (image is { Length: > 0 })
        {
            var (path, error) = await SaveImageAsync(image);
            if (error is not null) ModelState.AddModelError("image", error);
            else uploadedPath = path;
        }

        if (!ModelState.IsValid) return View(model);

        Product product;
        if (model.Id == 0)
        {
            product = new Product();
            _db.Products.Add(product);
        }
        else
        {
            var existing = await _db.Products
                .Include(p => p.Images)
                .FirstOrDefaultAsync(p => p.Id == model.Id);

            if (existing is null) return NotFound();
            product = existing;
        }

        product.Slug = model.Slug;
        product.NameAr = model.NameAr;
        product.NameEn = model.NameEn;
        product.DescriptionAr = model.DescriptionAr;
        product.DescriptionEn = model.DescriptionEn;
        product.Price = model.Price;
        product.UnitAr = model.UnitAr;
        product.UnitEn = model.UnitEn;
        product.InStock = model.InStock;
        product.IsActive = model.IsActive;
        product.SortOrder = model.SortOrder;
        product.UpdatedAt = DateTime.UtcNow;

        var primary = product.Images.FirstOrDefault(i => i.IsPrimary);

        if (uploadedPath is not null)
        {
            if (primary is null)
            {
                primary = new ProductImage { IsPrimary = true, SortOrder = 1 };
                product.Images.Add(primary);
            }
            primary.Path = uploadedPath;
        }

        if (primary is not null)
        {
            primary.FocalY = model.FocalY;
            primary.AltAr = model.NameAr;
            primary.AltEn = model.NameEn;
        }

        await _db.SaveChangesAsync();

        TempData["Message"] = $"Saved {product.NameEn}.";
        return RedirectToAction(nameof(Products));
    }

    /// <summary>
    /// Writes an uploaded image into wwwroot and returns its relative path.
    /// The original filename is never used — a supplied name could contain path
    /// segments, overwrite an existing file, or carry a misleading extension.
    /// </summary>
    private async Task<(string? Path, string? Error)> SaveImageAsync(IFormFile file)
    {
        if (file.Length > MaxImageBytes)
            return (null, "Image must be 8 MB or smaller.");

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!AllowedImageExtensions.Contains(extension))
            return (null, "Use a JPG, PNG or WebP image.");

        var folder = Path.Combine(_env.WebRootPath, "img", "products");
        Directory.CreateDirectory(folder);

        var fileName = $"{Guid.NewGuid():N}{extension}";
        var fullPath = Path.Combine(folder, fileName);

        await using (var stream = System.IO.File.Create(fullPath))
        {
            await file.CopyToAsync(stream);
        }

        return ($"img/products/{fileName}", null);
    }

    // ---------- pickup locations ----------

    public async Task<IActionResult> Locations()
    {
        var locations = await _db.PickupLocations
            .OrderBy(l => l.SortOrder)
            .AsNoTracking()
            .ToListAsync();

        return View(locations);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveLocation(PickupLocation model)
    {
        if (!ModelState.IsValid)
        {
            TempData["Message"] = "Check the location details and try again.";
            return RedirectToAction(nameof(Locations));
        }

        if (model.Id == 0)
        {
            _db.PickupLocations.Add(model);
        }
        else
        {
            var existing = await _db.PickupLocations.FindAsync(model.Id);
            if (existing is null) return NotFound();

            existing.NameAr = model.NameAr;
            existing.NameEn = model.NameEn;
            existing.MapUrl = model.MapUrl;
            existing.Latitude = model.Latitude;
            existing.Longitude = model.Longitude;
            existing.IsActive = model.IsActive;
            existing.SortOrder = model.SortOrder;
        }

        await _db.SaveChangesAsync();
        TempData["Message"] = "Pickup locations saved.";
        return RedirectToAction(nameof(Locations));
    }

    // ---------- orders (placeholder until phase 3) ----------

    public async Task<IActionResult> Orders()
    {
        var orders = await _db.Orders
            .Include(o => o.Items)
            .Include(o => o.PickupLocation)
            .OrderByDescending(o => o.CreatedAt)
            .AsNoTracking()
            .ToListAsync();

        return View(orders);
    }
}
