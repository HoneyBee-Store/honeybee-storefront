using System.ComponentModel.DataAnnotations;

namespace HoneyBee.Web.Models;

public class CartLineViewModel
{
    public Product Product { get; set; } = null!;
    public decimal SizeKg { get; set; }
    public int Quantity { get; set; }

    /// <summary>Per-kg rate × jar size — the price of one jar this size.</summary>
    public decimal? UnitPrice => Product.Price * SizeKg;
    public decimal? LineTotal => UnitPrice * Quantity;
}

public class CartViewModel
{
    public List<CartLineViewModel> Lines { get; set; } = new();

    public decimal? Total => Lines.Any(l => l.LineTotal is null)
        ? null
        : Lines.Sum(l => l.LineTotal!.Value);

    public int TotalItems => Lines.Sum(l => l.Quantity);
    public bool IsEmpty => Lines.Count == 0;
}

public class CheckoutViewModel
{
    [Required(ErrorMessage = "Please enter your name.")]
    [MaxLength(120)]
    public string CustomerName { get; set; } = "";

    [Required(ErrorMessage = "Please enter your phone number.")]
    [MaxLength(30)]
    public string Phone { get; set; } = "";

    [Required(ErrorMessage = "Please choose where to collect your order.")]
    public int PickupLocationId { get; set; }

    [MaxLength(1000)]
    public string? CustomerNotes { get; set; }

    public CartViewModel Cart { get; set; } = new();
    public List<PickupLocation> PickupLocations { get; set; } = new();
}
