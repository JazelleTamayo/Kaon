using System.ComponentModel.DataAnnotations.Schema;

namespace Kaon.Models;

// A dish the owner picked from their catalog to sell on a given day.
// Today's menu is just the rows where Date == today; nothing resets them automatically,
// they simply stop showing once the day has moved on.
public class DailyMenuEntry
{
    public int Id { get; set; }

    public int MenuItemId { get; set; }
    public MenuItem? MenuItem { get; set; }

    public DateTime Date { get; set; } = DateTime.Today;

    // Portions left today. Null means no limit.
    public int? Stock { get; set; }

    public DishReadyState ReadyState { get; set; } = DishReadyState.ReadyToServe;

    [NotMapped]
    public bool CanOrder => Stock == null || Stock > 0;
}
