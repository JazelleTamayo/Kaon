using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Kaon.Models;

// One row: the carinderia's store page settings (Module 2)
public class StoreSettings
{
    public int Id { get; set; }

    [Required(ErrorMessage = "Please enter the store name."), StringLength(100)]
    public string Name { get; set; } = "Jamaica Foods";

    [StringLength(100)]
    public string? Hours { get; set; } = "7:00 AM - 7:00 PM";

    [StringLength(20)]
    public string? Phone { get; set; }

    public bool IsOpen { get; set; } = true;
    public bool PickupEnabled { get; set; } = true;
    public bool DeliveryEnabled { get; set; } = true;

    [Column(TypeName = "decimal(10,2)"), Range(0, 500, ErrorMessage = "Enter a fee from 0 to 500.")]
    public decimal DeliveryFee { get; set; } = 30;

    [StringLength(200)]
    public string? DeliveryArea { get; set; }

    // Internal: the starter food packages have been added once
    public bool StarterPackagesAdded { get; set; }
}
