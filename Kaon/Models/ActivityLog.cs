using System.ComponentModel.DataAnnotations;

namespace Kaon.Models;

// What the Kaon team did, and when. It never holds customers' personal details or passwords.
public class ActivityLog
{
    public int Id { get; set; }

    public DateTime At { get; set; } = DateTime.UtcNow;

    public int AdminId { get; set; }

    [StringLength(100)]
    public string AdminName { get; set; } = "";

    [Required, StringLength(250)]
    public string Action { get; set; } = "";

    // The carinderia it was about, if any (the name is kept as written at the time)
    public int? StoreId { get; set; }

    [StringLength(100)]
    public string? StoreName { get; set; }
}
