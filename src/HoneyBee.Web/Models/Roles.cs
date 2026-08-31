namespace HoneyBee.Web.Models;

public static class Roles
{
    /// <summary>
    /// The only role that exists. Customers deliberately have no role at all —
    /// being signed in is what identifies them, and adding a "Customer" role
    /// would be a second thing to keep in sync for no benefit.
    /// </summary>
    public const string Admin = "Admin";
}
