using System.ComponentModel.DataAnnotations;

namespace Kaon.Models;

// Module 12: one row each time someone opens a carinderia's page by scanning a printed QR code.
// Printed codes open /q/{slug}?s=stand (and so on), which records the scan and forwards to /c/{slug}.
// No personal details are kept: only which carinderia, when, which printout, and whether a sign-up followed.
public class QrScan
{
    public int Id { get; set; }

    public int StoreId { get; set; }

    public DateTime At { get; set; } = DateTime.UtcNow;

    // Which printout was scanned: poster, stand, counter, flyer or qr (the downloaded picture)
    [StringLength(20)]
    public string Source { get; set; } = "qr";

    // Set when the person who scanned went on to create a customer account
    public int? SignedUpUserId { get; set; }
}

public static class QrSources
{
    public const string Default = "qr";

    // Printouts the owner (or the Kaon admin) can print, in the order they are shown
    public static readonly (string Key, string Name, string Use)[] Printables =
    {
        ("poster", "Wall poster", "A4 poster for the wall or the door"),
        ("stand", "Table stand", "Fold in half and stand it on a table"),
        ("counter", "Counter sign", "A smaller sign beside the cashier"),
        ("flyer", "Takeaway flyers", "Four flyers on one page, cut and hand out with orders")
    };

    public static string Clean(string? source) =>
        source != null && (source == Default || Printables.Any(p => p.Key == source)) ? source : Default;

    public static string Name(string source) =>
        source == Default ? "Downloaded QR" : Printables.FirstOrDefault(p => p.Key == source).Name ?? source;
}

public class QrStats
{
    public int Today { get; set; }
    public int Last7Days { get; set; }
    public int Last30Days { get; set; }
    public int Total { get; set; }
    public int SignUps { get; set; }
    public List<(string Source, int Count)> BySource { get; set; } = new();
}

public class PrintableVm
{
    public StoreSettings Store { get; set; } = new();
    public string Kind { get; set; } = "poster";
    public string QrUrl { get; set; } = "";     // the picture
    public string Link { get; set; } = "";      // the plain web address printed under the code
    public string ScanUrl { get; set; } = "";   // what the code itself opens (counts the scan)
    public string BackUrl { get; set; } = "";
    public string BackText { get; set; } = "Back";
}
