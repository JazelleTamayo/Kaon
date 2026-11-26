using System.ComponentModel.DataAnnotations;

namespace Kaon.Models;

// Module 7: a delivery rider the carinderia works with (its own people or a local courier).
// Riders have no Kaon account. For each delivery they get a private link (see RiderController).
public class Rider
{
    public int Id { get; set; }

    public int StoreId { get; set; }

    [Required(ErrorMessage = "Please enter the rider's name."), StringLength(60)]
    public string Name { get; set; } = "";

    [Required(ErrorMessage = "Please enter the rider's number."), StringLength(20)]
    public string Phone { get; set; } = "";

    // Removed riders stay in old orders (orders keep a copy of the name and number)
    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
