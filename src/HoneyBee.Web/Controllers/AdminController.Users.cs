using HoneyBee.Web.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HoneyBee.Web.Controllers;

/// <summary>
/// Managing the people who can sign in.
///
/// Everything goes through UserManager rather than the tables directly. That
/// is not ceremony: passwords are stored as a hash, and the normalised name
/// columns that sign-in actually matches against are maintained by Identity.
/// Editing those by hand produces an account that looks right in the database
/// and cannot sign in — or worse, one that crashes the login page.
/// </summary>
public partial class AdminController
{
    private UserManager<AppUser> Users => _signIn.UserManager;

    /// <summary>
    /// Customers and the owner share one table; the Admin role is what
    /// separates them, so this lists everyone.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> People()
    {
        var users = await _db.Users.AsNoTracking().OrderBy(u => u.CreatedAt).ToListAsync();
        var adminIds = await AdminIdsAsync();
        var meId = Users.GetUserId(User);

        var rows = users.Select(u => new UserRowViewModel
        {
            Id = u.Id,
            UserName = u.UserName ?? "",
            FullName = u.FullName,
            Phone = u.PhoneNumber,
            Email = u.Email,
            IsAdmin = adminIds.Contains(u.Id),
            CreatedAt = u.CreatedAt,
            LockedUntil = u.LockoutEnd > DateTimeOffset.UtcNow ? u.LockoutEnd : null,
            IsSelf = u.Id == meId
        }).ToList();

        return View(rows);
    }

    [HttpGet]
    public IActionResult CreateUser() => View("EditUser", new UserEditViewModel());

    [HttpGet]
    public async Task<IActionResult> EditUser(string id)
    {
        var user = await Users.FindByIdAsync(id);
        if (user is null) return NotFound();

        var adminIds = await AdminIdsAsync();

        return View(new UserEditViewModel
        {
            Id = user.Id,
            FullName = user.FullName,
            Phone = user.PhoneNumber ?? "",
            Email = user.Email,
            UserName = user.UserName,
            IsAdmin = adminIds.Contains(user.Id),
            IsSelf = user.Id == Users.GetUserId(User),
            IsLastAdmin = adminIds.Contains(user.Id) && adminIds.Count == 1
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditUser(UserEditViewModel model)
    {
        if (!PhoneNumbers.LooksValid(model.Phone))
        {
            ModelState.AddModelError(nameof(model.Phone),
                "Enter a Jordanian mobile number, e.g. 0790000000.");
        }

        // Required for a new account, optional on an edit: blank keeps the
        // existing password, because only a hash is stored and it cannot be
        // read back to pre-fill the form.
        if (model.IsNew && string.IsNullOrWhiteSpace(model.Password))
        {
            ModelState.AddModelError(nameof(model.Password), "A password is needed for a new account.");
        }

        if (!ModelState.IsValid) return View(await FillUserFlagsAsync(model));

        var phone = PhoneNumbers.Normalise(model.Phone);
        var userName = string.IsNullOrWhiteSpace(model.UserName) ? phone : model.UserName.Trim();

        // Trimmed. The owner is setting a password for someone else and then
        // reading it out to them, and a space picked up from a paste is
        // invisible in this form but stored in the hash — after which the
        // password they were given never works and nothing says why.
        var password = model.Password?.Trim();

        AppUser user;

        if (model.IsNew)
        {
            if (await Users.FindByNameAsync(userName) is not null)
            {
                ModelState.AddModelError(nameof(model.UserName), "That sign-in name is already taken.");
                return View(await FillUserFlagsAsync(model));
            }

            user = new AppUser
            {
                UserName = userName,
                PhoneNumber = phone,
                Email = model.Email?.Trim(),
                EmailConfirmed = true,
                FullName = model.FullName.Trim()
            };

            var created = await Users.CreateAsync(user, password!);
            if (!created.Succeeded) return View(await FillUserFlagsAsync(model, created));
        }
        else
        {
            var found = await Users.FindByIdAsync(model.Id!);
            if (found is null) return NotFound();
            user = found;

            user.FullName = model.FullName.Trim();
            user.PhoneNumber = phone;
            user.Email = model.Email?.Trim();

            if (!string.Equals(user.UserName, userName, StringComparison.Ordinal))
            {
                var renamed = await Users.SetUserNameAsync(user, userName);
                if (!renamed.Succeeded) return View(await FillUserFlagsAsync(model, renamed));
            }

            var updated = await Users.UpdateAsync(user);
            if (!updated.Succeeded) return View(await FillUserFlagsAsync(model, updated));

            if (!string.IsNullOrWhiteSpace(password))
            {
                // Reset, not change: the owner is setting someone else's
                // password and has no way to supply the old one.
                var token = await Users.GeneratePasswordResetTokenAsync(user);
                var reset = await Users.ResetPasswordAsync(user, token, password);
                if (!reset.Succeeded) return View(await FillUserFlagsAsync(model, reset));
            }
        }

        await ApplyAdminRoleAsync(user, model.IsAdmin);

        TempData["Message"] = model.IsNew ? $"Added {user.UserName}." : $"Saved {user.UserName}.";
        return RedirectToAction(nameof(People));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteUser(string id)
    {
        var user = await Users.FindByIdAsync(id);
        if (user is null) return NotFound();

        if (user.Id == Users.GetUserId(User))
        {
            TempData["Message"] = "You cannot delete the account you are signed in with.";
            return RedirectToAction(nameof(People));
        }

        var adminIds = await AdminIdsAsync();

        if (adminIds.Contains(user.Id) && adminIds.Count == 1)
        {
            TempData["Message"] = "That is the only admin account — deleting it would lock everyone out.";
            return RedirectToAction(nameof(People));
        }

        // Orders keep their own copy of the customer's name and phone, so past
        // orders stay readable after the account goes.
        await Users.DeleteAsync(user);

        TempData["Message"] = $"Deleted {user.UserName}.";
        return RedirectToAction(nameof(People));
    }

    /// <summary>Clears the lockout that follows five failed sign-ins.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UnlockUser(string id)
    {
        var user = await Users.FindByIdAsync(id);
        if (user is null) return NotFound();

        await Users.SetLockoutEndDateAsync(user, null);
        await Users.ResetAccessFailedCountAsync(user);

        TempData["Message"] = $"Unlocked {user.UserName}.";
        return RedirectToAction(nameof(People));
    }

    private async Task<List<string>> AdminIdsAsync() =>
        (await Users.GetUsersInRoleAsync(Roles.Admin)).Select(u => u.Id).ToList();

    /// <summary>
    /// Restores the flags the form needs after a failed post, and surfaces any
    /// Identity errors — most often the password rules from Program.cs.
    /// </summary>
    private async Task<UserEditViewModel> FillUserFlagsAsync(
        UserEditViewModel model, IdentityResult? result = null)
    {
        if (result is not null)
        {
            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Description);
            }
        }

        var adminIds = await AdminIdsAsync();
        model.IsSelf = model.Id == Users.GetUserId(User);
        model.IsLastAdmin = model.Id is not null && adminIds.Contains(model.Id) && adminIds.Count == 1;
        return model;
    }

    private async Task ApplyAdminRoleAsync(AppUser user, bool shouldBeAdmin)
    {
        var isAdmin = await Users.IsInRoleAsync(user, Roles.Admin);

        if (shouldBeAdmin && !isAdmin)
        {
            await Users.AddToRoleAsync(user, Roles.Admin);
            return;
        }

        if (!shouldBeAdmin && isAdmin)
        {
            // Refused when it would leave nobody able to reach the admin at all.
            var adminIds = await AdminIdsAsync();
            if (adminIds.Count > 1) await Users.RemoveFromRoleAsync(user, Roles.Admin);
        }
    }
}
