namespace Kaon.Models;

public enum PaymentMethod
{
    Cash,       // pay in person: at pickup, or when the food is delivered
    Cashless    // pay in advance (GCash, bank): the customer sends proof of payment
}

public enum PaymentStatus
{
    Unpaid,
    ProofSent,  // cashless: customer sent a screenshot, waiting for the owner to confirm
    Paid,
    // Food packages only: the 50% deposit is confirmed, the rest is due at pickup/delivery.
    // Appended at the end so existing stored values keep their numbers.
    DepositPaid
}
