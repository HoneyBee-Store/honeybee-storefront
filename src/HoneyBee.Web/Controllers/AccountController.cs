using HoneyBee.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;

namespace HoneyBee.Web.Controllers;

/// <summary>
/// Customer accounts. Separate from AdminController because the two audiences
/// sign in differently — customers by phone, the owner by email — and share
/// none of the same pages.
/// </summary>
public class AccountController : Controller
{
    private readonly UserManager<AppUser> _users;
    private readonly SignInManager<AppUser> _signIn;
    private readonly IStringLocalizer<SharedResource> _l;

    public AccountController(
        UserManager<AppUser> users,
        SignInManager<AppUser> signIn,
        IStringLocalizer<SharedResource> l)
    {
        _users = users;
        _signIn = signIn;
        _l = l;
    }

    // ---------- register ----------

    [HttpGet]
    public IActionResult Register(string? returnUrl)
    {
        if (User.Identity?.IsAuthenticated == true) return RedirectToAction(nameof(Profile));
        return View(new CustomerRegisterViewModel { ReturnUrl = returnUrl });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Register(CustomerRegisterViewModel model)
    {
        if (!PhoneNumbers.LooksValid(model.Phone))
        {
            ModelState.AddModelError(nameof(model.Phone),
                _l["Enter a Jordanian mobile number, e.g. 0790000000."]);
        }

        if (!ModelState.IsValid) return View(model);

        // Stored normalised so 0790000000 and +962790000000 can't become two
        // accounts for the same person.
        var phone = PhoneNumbers.Normalise(model.Phone);

        if (await _users.FindByNameAsync(phone) is not null)
        {
            ModelState.AddModelError(nameof(model.Phone),
                _l["An account already uses this number."]);
            return View(model);
        }

        var user = new AppUser
        {
            UserName = phone,        // what Identity signs in against
            PhoneNumber = phone,
            Email = model.Email.Trim(),
            FullName = model.FullName.Trim()
        };

        var result = await _users.CreateAsync(user, model.Password);

        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
            {
                // Email uniqueness is enforced by Identity, not by the check above.
                var field = error.Code.Contains("Email") ? nameof(model.Email) : string.Empty;
                ModelState.AddModelError(field, error.Description);
            }
            return View(model);
        }

        await _signIn.SignInAsync(user, isPersistent: true);

        return LocalRedirect(Url.IsLocalUrl(model.ReturnUrl) ? model.ReturnUrl! : "/");
    }

    // ---------- sign in ----------

    [HttpGet]
    public IActionResult Login(string? returnUrl)
    {
        if (User.Identity?.IsAuthenticated == true) return RedirectToAction(nameof(Profile));
        return View(new CustomerLoginViewModel { ReturnUrl = returnUrl });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(CustomerLoginViewModel model)
    {
        if (!ModelState.IsValid) return View(model);

        // Customers sign in with a phone number, the owner with a username.
        // Deciding which by the shape of the input keeps this to a single
        // attempt — trying both in turn would burn two of the five tries
        // before lockout on every failed sign-in.
        var identifier = PhoneNumbers.LooksValid(model.Phone)
            ? PhoneNumbers.Normalise(model.Phone)
            : model.Phone.Trim();

        var user = await _users.FindByNameAsync(identifier) ?? await FindByPhoneAsync(model.Phone);

        // The lookup is resolved to an account first so that the phone still
        // works when the owner gave that account its own sign-in name from the
        // admin. The form promises "sign in with your phone", and before this
        // that promise quietly failed for every such account.
        var result = user is null
            ? Microsoft.AspNetCore.Identity.SignInResult.Failed
            : await _signIn.PasswordSignInAsync(
                  user, model.Password, model.RememberMe, lockoutOnFailure: true);

        if (result.Succeeded)
        {
            // An explicit destination wins; otherwise send the owner to the
            // admin and everyone else to the shop.
            if (Url.IsLocalUrl(model.ReturnUrl)) return LocalRedirect(model.ReturnUrl!);

            if (await _users.IsInRoleAsync(user!, Roles.Admin))
            {
                return RedirectToAction(nameof(AdminController.Products), "Admin");
            }

            return Redirect("/");
        }

        // One message for both "no such number" and "wrong password", so the
        // form can't be used to discover which numbers are registered.
        ModelState.AddModelError(string.Empty,
            result.IsLockedOut
                ? _l["Too many attempts. Please try again later."]
                : _l["Incorrect phone number or password."]);

        return View(model);
    }

    /// <summary>
    /// Finds an account by the phone number typed into the sign-in form.
    ///
    /// Refuses to guess when two accounts share a number: signing somebody in
    /// as whichever one happened to come back first would be worse than saying
    /// the details are wrong.
    /// </summary>
    private async Task<AppUser?> FindByPhoneAsync(string typed)
    {
        if (!PhoneNumbers.LooksValid(typed)) return null;

        var phone = PhoneNumbers.Normalise(typed);

        var matches = _users.Users
            .Where(u => u.PhoneNumber == phone)
            .Take(2)
            .ToList();

        return matches.Count == 1 ? matches[0] : null;
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await _signIn.SignOutAsync();
        return RedirectToAction(nameof(HomeController.Index), "Home");
    }

    /// <summary>
    /// Shown when a signed-in customer reaches somewhere only the owner can go.
    /// </summary>
    [HttpGet]
    public IActionResult Denied() => View();

    // ---------- profile ----------

    [Authorize]
    [HttpGet]
    public async Task<IActionResult> Profile()
    {
        var user = await _users.GetUserAsync(User);
        if (user is null) return RedirectToAction(nameof(Login));

        return View(user);
    }
}
