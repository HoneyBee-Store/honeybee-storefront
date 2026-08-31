using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;

namespace HoneyBee.Web.Models;

/// <summary>
/// Customers and the shop owner share one user table; what separates them is
/// the Admin role, not the type.
///
/// Identity signs in against <see cref="IdentityUser.UserName"/>, so the phone
/// number is stored there as well as in PhoneNumber — that is what makes
/// "sign in with your phone" work without a custom sign-in path.
/// </summary>
public class AppUser : IdentityUser
{
    [PersonalData]
    public string FullName { get; set; } = "";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public static class PhoneNumbers
{
    /// <summary>
    /// Reduces a Jordanian number to one canonical form, so 0780364203,
    /// 078 036 4203 and +962780364203 are recognised as the same person
    /// rather than becoming three separate accounts.
    /// </summary>
    public static string Normalise(string input)
    {
        var digits = Regex.Replace(input ?? "", @"[^\d+]", "");

        if (digits.StartsWith("00")) digits = "+" + digits[2..];

        if (digits.StartsWith("+")) return digits;
        if (digits.StartsWith("962")) return "+" + digits;
        if (digits.StartsWith("0")) return "+962" + digits[1..];
        if (digits.Length == 9) return "+962" + digits;   // 7XXXXXXXX

        return digits;
    }

    /// <summary>Jordanian mobiles are +962 7 followed by 8 digits.</summary>
    public static bool LooksValid(string input)
    {
        var n = Normalise(input);
        return Regex.IsMatch(n, @"^\+9627\d{8}$");
    }
}
