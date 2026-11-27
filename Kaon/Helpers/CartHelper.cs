using System.Text.Json;

namespace Kaon.Helpers;

public class CartLine
{
    public int MenuItemId { get; set; }
    public string Name { get; set; } = "";
    public decimal Price { get; set; }
    public int Quantity { get; set; } = 1;

    // The dish's photo, so the cart can show it
    public string? ImageUrl { get; set; }

    // Food packages: which size or portion was chosen
    public int? OptionId { get; set; }
    public string? OptionLabel { get; set; }
    public bool IsPackage { get; set; }

    // Food packages only: when the customer needs it, chosen when the package was added to the cart
    public DateTime? ScheduledFor { get; set; }
}

public static class CartHelper
{
    private const string Key = "cart";

    public static List<CartLine> Get(ISession session)
    {
        var json = session.GetString(Key);
        return json == null
            ? new List<CartLine>()
            : JsonSerializer.Deserialize<List<CartLine>>(json) ?? new List<CartLine>();
    }

    public static void Save(ISession session, List<CartLine> cart)
        => session.SetString(Key, JsonSerializer.Serialize(cart));

    public static void Clear(ISession session) => session.Remove(Key);
}