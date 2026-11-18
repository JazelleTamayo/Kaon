namespace Kaon.Models;

public enum OrderStatus
{
    Pending,
    Preparing,
    Ready,
    OutForDelivery,
    Completed,
    Cancelled
}

public enum OrderType
{
    Pickup,
    Delivery
}
