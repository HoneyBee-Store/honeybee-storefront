using Microsoft.AspNetCore.Identity;

namespace HoneyBee.Web.Data;

/// <summary>
/// Creates the first admin account from configuration, because the site has no
/// registration page — customers never sign in, and a public "create the first
/// admin" page is a well-known way to lose a site to whoever finds it first.
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
        UserManager<IdentityUser> users,
        IConfiguration config,
        ILogger logger)
    {
        var email = config["Admin:Email"];
        var password = config["Admin:Password"];

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            logger.LogWarning(
                "Admin:Email / Admin:Password not configured — no admin account created. " +
                "You will not be able to sign in at /Admin/Login until they are set.");
            return;
        }

        if (await users.FindByEmailAsync(email) is not null) return;

        var user = new IdentityUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true
        };

        var result = await users.CreateAsync(user, password);

        if (result.Succeeded)
        {
            logger.LogInformation("Created admin account {Email}.", email);
        }
        else
        {
            // Usually the password failing the strength rules in Program.cs.
            logger.LogError("Could not create admin account: {Errors}",
                string.Join("; ", result.Errors.Select(e => e.Description)));
        }
    }
}
