namespace Kaon.Helpers;

// How many days ahead a food package must be ordered, by its price.
// Bigger/pricier packages need more notice so the owner can source ingredients.
public static class PackageLeadTime
{
    public static int DaysFor(decimal price) => price switch
    {
        >= 5000m => 10,
        >= 1000m => 5,
        _ => 3
    };
}
