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
    Paid
}
