namespace Kaon.Models;

public enum OrderStatus
{
    Pending,
    Preparing,
    Ready,
    OutForDelivery,
    Completed,
    Cancelled,
    // Food packages only: the owner has reviewed and accepted the pre-order, which now
    // waits in the reserved queue until its pickup/delivery date. Appended at the end
    // so existing stored values keep their numbers.
    Confirmed
}

public enum OrderType
{
    Pickup,
    Delivery
}
