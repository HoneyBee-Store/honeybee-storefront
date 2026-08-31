using HoneyBee.Web.Models;
using Microsoft.AspNetCore.Identity;

namespace HoneyBee.Web.Data;

/// <summary>
/// Creates the Admin role and the first admin account from configuration.
/// There is no registration page for admins — customers have one, but a public
/// "create the first admin" page is a well-known way to lose a site to whoever
/// finds it first.
///
/// Set these outside the repo:
///   dotnet user-secrets set "Admin:Email" "you@example.com"
///   dotnet user-secrets set "Admin:Password" "a long passphrase"
///
/// In production supply Admin__Email / Admin__Password as environment
/// variables. If either is missing, no account is created and the app still
/// starts — the storefront does not depend on it.
/// </summary>
public static class AdminSeeder
{
    public static async Task SeedAsync(
        UserManager<AppUser> users,
        RoleManager<IdentityRole> roles,
        IConfiguration config,
        ILogger logger)
    {
        if (!await roles.RoleExistsAsync(Roles.Admin))
        {
            await roles.CreateAsync(new IdentityRole(Roles.Admin));
        }

        var email = config["Admin:Email"];
        var password = config["Admin:Password"];

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            logger.LogWarning(
                "Admin:Email / Admin:Password not configured — no admin account created. " +
                "You will not be able to sign in at /Admin/Login until they are set.");
            return;
        }

        var user = await users.FindByEmailAsync(email);

        if (user is null)
        {
            user = new AppUser
            {
                UserName = email,   // admins sign in with email; customers use their phone
                Email = email,
                EmailConfirmed = true,
                FullName = "Shop owner"
            };

            var result = await users.CreateAsync(user, password);

            if (!result.Succeeded)
            {
                // Usually the password failing the strength rules in Program.cs.
                logger.LogError("Could not create admin account: {Errors}",
                    string.Join("; ", result.Errors.Select(e => e.Description)));
                return;
            }

            logger.LogInformation("Created admin account {Email}.", email);
        }

        // Runs even for an existing account, so an admin created before roles
        // existed still gets promoted rather than being locked out.
        if (!await users.IsInRoleAsync(user, Roles.Admin))
        {
            await users.AddToRoleAsync(user, Roles.Admin);
            logger.LogInformation("Granted the Admin role to {Email}.", email);
        }
    }
}
