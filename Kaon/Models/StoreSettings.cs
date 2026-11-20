using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Kaon.Models;

// One row PER carinderia: the carinderia's store page settings (Module 2)
public class StoreSettings
{
    public int Id { get; set; }

    // The carinderia's own web address: /c/{Slug}. Its link and QR code open this carinderia only.
    [StringLength(60)]
    public string Slug { get; set; } = "";

    [Required(ErrorMessage = "Please enter the store name."), StringLength(100)]
    public string Name { get; set; } = "Jamaica Foods";

    [StringLength(100)]
    public string? Hours { get; set; } = "7:00 AM - 7:00 PM";

    [StringLength(20)]
    public string? Phone { get; set; }

    public bool IsOpen { get; set; } = true;

    // What the carinderia offers right now (customers can't see or order what is off)
    public bool OffersDailyMenu { get; set; } = true;
    public bool OffersPackages { get; set; } = true;
    public bool PickupEnabled { get; set; } = true;
    public bool DeliveryEnabled { get; set; } = true;

    [Column(TypeName = "decimal(10,2)"), Range(0, 500, ErrorMessage = "Enter a fee from 0 to 500.")]
    public decimal DeliveryFee { get; set; } = 30;

    [StringLength(200)]
    public string? DeliveryArea { get; set; }

    // Payments the carinderia accepts
    public bool AcceptsCash { get; set; } = true;
    public bool AcceptsCashless { get; set; } = true;

    [StringLength(80)]
    public string? GCashName { get; set; } = "Jamaica Foods";

    [StringLength(30)]
    // SAMPLE until the owner sets the real one on the Store page (or in the private setting Kaon:Demo:GCashNumber)
    public string? GCashNumber { get; set; } = "09XX XXX XXXX";

    // Another bank or e-wallet, if the carinderia has one
    [StringLength(60)]
    public string? BankName { get; set; }

    [StringLength(80)]
    public string? BankAccountName { get; set; }

    [StringLength(40)]
    public string? BankAccountNumber { get; set; }

    // File name of the QR code picture customers can scan to pay (kept outside wwwroot)
    [StringLength(100)]
    public string? PaymentQr { get; set; }

    [StringLength(200)]
    public string? PaymentNote { get; set; }

    // Set while a customer is looking (never saved): ordering is paused because no owner can handle orders
    [NotMapped]
    public bool PausedByBilling { get; set; }

    // Internal: today's menu was carried over from the old menu once (see DemoData)
    public bool DailyMenuSeeded { get; set; }

    // The Kaon team can pause a carinderia (for example, an unpaid account or a request to close).
    // Customers see it as not taking orders. The owner can still log in.
    public bool SuspendedByAdmin { get; set; }

    // Does the carinderia have at least one way to be paid in advance?
    [NotMapped]
    public bool HasCashlessDetails =>
        !string.IsNullOrWhiteSpace(GCashNumber) || !string.IsNullOrWhiteSpace(BankAccountNumber) || !string.IsNullOrEmpty(PaymentQr);

    [NotMapped]
    public bool CashlessAvailable => AcceptsCashless && HasCashlessDetails;

    // Internal: the starter food packages have been added once
    public bool StarterPackagesAdded { get; set; }
}
