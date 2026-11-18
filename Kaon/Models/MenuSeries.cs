namespace Kaon.Models;

// The "series" a food belongs to, so customers can browse by type.
public static class MenuSeries
{
    public const string Other = "Other";

    public static readonly string[] All =
    {
        "Chicken", "Pork", "Beef", "Seafood", "Vegetables", "Noodles & Pasta", Other
    };

    public static string Of(string? series) =>
        string.IsNullOrWhiteSpace(series) ? Other : series;
}
