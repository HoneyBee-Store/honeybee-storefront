using System.Text.Json;

namespace HoneyBee.Web.Models;

/// <summary>
/// One line in the basket. Deliberately stores only what the customer chose —
/// the name and price are looked up fresh from the database on every render,
/// so a price change while someone is shopping can't be missed.
/// </summary>
public class CartLine
{
    public int ProductId { get; set; }
    public decimal SizeKg { get; set; } = 1m;
    public int Quantity { get; set; } = 1;

    /// <summary>Same product in two jar sizes is two separate lines.</summary>
    public string Key => $"{ProductId}:{SizeKg}";
}

public class Cart
{
    public List<CartLine> Lines { get; set; } = new();

    public int TotalItems => Lines.Sum(l => l.Quantity);

    public void Add(int productId, decimal sizeKg, int quantity)
    {
        var existing = Lines.FirstOrDefault(l => l.ProductId == productId && l.SizeKg == sizeKg);

        if (existing is null)
        {
            Lines.Add(new CartLine { ProductId = productId, SizeKg = sizeKg, Quantity = quantity });
        }
        else
        {
            existing.Quantity = Math.Min(existing.Quantity + quantity, 999);
        }
    }

    public void SetQuantity(int productId, decimal sizeKg, int quantity)
    {
        var line = Lines.FirstOrDefault(l => l.ProductId == productId && l.SizeKg == sizeKg);
        if (line is null) return;

        if (quantity <= 0) Lines.Remove(line);
        else line.Quantity = Math.Min(quantity, 999);
    }

    public void Remove(int productId, decimal sizeKg) =>
        Lines.RemoveAll(l => l.ProductId == productId && l.SizeKg == sizeKg);

    public void Clear() => Lines.Clear();
}

/// <summary>
/// The cart lives in session rather than the database: it is scratch data, and
/// writing every "+1" to SQL would be a lot of noise for something most
/// visitors abandon. It becomes an Order row only at checkout.
/// </summary>
public static class CartSession
{
    private const string Key = "cart";

    public static Cart GetCart(this ISession session)
    {
        var json = session.GetString(Key);
        if (string.IsNullOrEmpty(json)) return new Cart();

        try { return JsonSerializer.Deserialize<Cart>(json) ?? new Cart(); }
        catch (JsonException) { return new Cart(); }   // shape changed between deploys
    }

    public static void SaveCart(this ISession session, Cart cart) =>
        session.SetString(Key, JsonSerializer.Serialize(cart));
}
