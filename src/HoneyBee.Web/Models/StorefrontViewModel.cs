namespace HoneyBee.Web.Models;

public class StorefrontViewModel
{
    public List<Product> Products { get; set; } = new();
    public List<PickupLocation> PickupLocations { get; set; } = new();

    /// <summary>Shown in the contact block. Hard-coded for now; phase 2 moves it into settings.</summary>
    public string Phone { get; } = "+962 78 036 4203";
    public string PhoneHref { get; } = "tel:+962780364203";
    public string Email { get; } = "khaledehmide3@gmail.com";
}
