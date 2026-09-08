using HoneyBee.Web.Data;
using HoneyBee.Web.Models;
using HoneyBee.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;

namespace HoneyBee.Web.Controllers;

// Role-gated, not just [Authorize]: customers can sign in too, and a bare
// [Authorize] would let any of them reach the admin.
[Authorize(Roles = Roles.Admin)]
public class AdminController : Controller
{
    private const long MaxImageBytes = 8 * 1024 * 1024;

    // Allowlist rather than a blocklist, and checked against the real extension
    // rather than whatever the browser claims the content type is.
    private static readonly string[] AllowedImageExtensions = [".jpg", ".jpeg", ".png", ".webp"];

    private readonly AppDbContext _db;
    private readonly SignInManager<AppUser> _signIn;
    private readonly IWebHostEnvironment _env;
    private readonly OrderNotifier _notifier;
    private readonly MailSettingsStore _mail;
    private readonly EmailPageGate _gate;
    private readonly StorageSettings _storage;

    public AdminController(AppDbContext db, SignInManager<AppUser> signIn,
                           IWebHostEnvironment env, OrderNotifier notifier,
                           MailSettingsStore mail, EmailPageGate gate,
                           StorageSettings storage)
    {
        _storage = storage;
        _db = db;
        _signIn = signIn;
        _env = env;
        _notifier = notifier;
        _mail = mail;
        _gate = gate;
    }

    /// <summary>
    /// The actions that count as "inside" the mail settings. Anything else
    /// drops the unlock, so arriving at Email from elsewhere in the admin
    /// always asks for the passphrase again — while saving and testing from
    /// within the page do not.
    /// </summary>
    private static readonly HashSet<string> MailActions = new(StringComparer.OrdinalIgnoreCase)
    {
        nameof(Email), nameof(Unlock), nameof(SendTestEmail), nameof(ClearMailPassword)
    };

    public override void OnActionExecuting(ActionExecutingContext context)
    {
        var action = context.ActionDescriptor.RouteValues["action"];

        if (action is null || !MailActions.Contains(action))
        {
            _gate.Lock(HttpContext.Session);
        }

        base.OnActionExecuting(context);
    }

    // ---------- email ----------

    /// <summary>
    /// Asks for the mail passphrase. Always renders — it never redirects to
    /// Email, so it cannot form a redirect pair with it.
    /// </summary>
    [HttpGet]
    public IActionResult Unlock(string? returnUrl)
    {
        return View(new EmailUnlockViewModel
        {
            ReturnUrl = returnUrl,
            LockedFor = _gate.LockedFor(HttpContext.Session),
            NotConfigured = !_gate.IsEnabled
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Unlock(EmailUnlockViewModel model)
    {
        if (_gate.TryUnlock(HttpContext.Session, model.Passphrase))
        {
            return LocalRedirect(Url.IsLocalUrl(model.ReturnUrl)
                ? model.ReturnUrl!
                : Url.Action(nameof(Email))!);
        }

        model.Passphrase = null;
        model.LockedFor = _gate.LockedFor(HttpContext.Session);
        model.AttemptsLeft = _gate.AttemptsLeft(HttpContext.Session);
        model.NotConfigured = !_gate.IsEnabled;
        model.Failed = model.LockedFor is null && !model.NotConfigured;

        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> Email()
    {
        // Rendered in place rather than redirected to. A redirect pair between
        // this action and Unlock loops forever if the two ever disagree about
        // the session — showing the dialog here cannot loop whatever happens.
        if (!_gate.IsUnlocked(HttpContext.Session)) return LockedView();

        return View(await BuildEmailViewModelAsync());
    }

    /// <summary>The passphrase dialog, shown in place of whatever was asked for.</summary>
    private IActionResult LockedView() => View("Unlock", new EmailUnlockViewModel
    {
        ReturnUrl = Url.Action(nameof(Email)),
        LockedFor = _gate.LockedFor(HttpContext.Session),
        NotConfigured = !_gate.IsEnabled
    });

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Email(EmailSettingsViewModel model)
    {
        // Checked on every write too: guarding only the GET would leave the
        // settings changeable by posting straight to this action.
        if (!_gate.IsUnlocked(HttpContext.Session)) return LockedView();

        if (!ModelState.IsValid)
        {
            return View(await BuildEmailViewModelAsync(model));
        }

        await _mail.SaveAsync(model.Provider, model.Host, model.Port, model.User,
                              model.From, model.Password, model.ApiKey);

        var saved = await BuildEmailViewModelAsync();
        saved.Saved = true;
        return View(saved);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ClearMailPassword()
    {
        if (!_gate.IsUnlocked(HttpContext.Session)) return LockedView();

        await _mail.ClearSecretsAsync();

        var model = await BuildEmailViewModelAsync();
        model.Saved = true;
        return View(nameof(Email), model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SendTestEmail()
    {
        if (!_gate.IsUnlocked(HttpContext.Session)) return LockedView();

        var model = await BuildEmailViewModelAsync();

        if (!model.IsConfigured)
        {
            model.Error = "Fill in the settings and save them first — there is nothing to test yet.";
            return View(nameof(Email), model);
        }

        try
        {
            await _notifier.SendAsync(
                model.SendsTo,
                "HoneyBee Shop — test email",
                "This is a test from the admin panel. If you are reading it, order emails will arrive here.");

            model.Sent = true;
        }
        catch (Exception ex)
        {
            // The real message, not a friendly one: this screen exists to
            // diagnose the settings, and "something went wrong" would defeat it.
            model.Error = ex.GetBaseException().Message;
        }

        return View(nameof(Email), model);
    }

    /// <summary>
    /// Fills the form from the stored settings. The password is never sent to
    /// the browser — only whether one is held.
    /// </summary>
    private async Task<EmailSettingsViewModel> BuildEmailViewModelAsync(
        EmailSettingsViewModel? posted = null)
    {
        var smtp = await _mail.GetAsync();

        return new EmailSettingsViewModel
        {
            Provider = posted?.Provider ?? smtp.Provider,
            Host = posted?.Host ?? smtp.Host,
            Port = posted?.Port ?? smtp.Port,
            User = posted?.User ?? smtp.User,
            From = posted?.From ?? smtp.From,
            IsConfigured = smtp.IsConfigured,
            HasStoredPassword = await _mail.HasStoredPasswordAsync(),
            HasStoredApiKey = await _mail.HasStoredApiKeyAsync(),
            SendsTo = _notifier.OrderEmail
        };
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
            model.UserName, model.Password, isPersistent: true, lockoutOnFailure: true);

        if (result.Succeeded)
        {
            return LocalRedirect(Url.IsLocalUrl(model.ReturnUrl)
                ? model.ReturnUrl!
                : Url.Action(nameof(Products))!);
        }

        // Deliberately vague: saying "no such account" tells an attacker which
        // usernames are real.
        ModelState.AddModelError(string.Empty,
            result.IsLockedOut
                ? "Too many attempts. Try again later."
                : "Incorrect username or password.");

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

        // Written outside the application folder when persistent storage is
        // configured: a deploy replaces wwwroot, so photos uploaded here would
        // otherwise disappear the next time the app is published.
        var (folder, urlPrefix) = _storage.HasUploads
            ? (_storage.UploadsPath!, StorageSettings.UploadsRequestPath.TrimStart('/'))
            : (Path.Combine(_env.WebRootPath, "img", "products"), "img/products");

        Directory.CreateDirectory(folder);

        var fileName = $"{Guid.NewGuid():N}{extension}";
        var fullPath = Path.Combine(folder, fileName);

        await using (var stream = System.IO.File.Create(fullPath))
        {
            await file.CopyToAsync(stream);
        }

        return ($"{urlPrefix}/{fileName}", null);
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
