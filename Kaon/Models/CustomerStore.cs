namespace Kaon.Models;

// One customer account works at every carinderia. This records which carinderias a customer
// has joined (by opening that carinderia's link or QR code). A carinderia sees only the
// customers who joined it.
public class CustomerStore
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public int StoreId { get; set; }
    public DateTime JoinedAt { get; set; } = DateTime.UtcNow;
}
