using Kaon.Models;

namespace Kaon.Helpers;

public static class OrderText
{
    public static string Label(OrderStatus s) => s switch
    {
        OrderStatus.OutForDelivery => "Out for Delivery",
        _ => s.ToString()
    };

    public static string Message(Order o) => o.Status switch
    {
        OrderStatus.Pending => "We got your order. Waiting for the carinderia to accept it.",
        OrderStatus.Preparing => "Your order is being prepared.",
        OrderStatus.Ready => o.OrderType == OrderType.Pickup
            ? "Your order is Ready. Pick it up."
            : "Your order is Ready. A rider will pick it up soon.",
        OrderStatus.OutForDelivery => "Your order is on its way.",
        OrderStatus.Cancelled => "This order was cancelled by the carinderia. Please contact them for details.",
        _ => "Done. Thank you for ordering with Kaon."
    };

    public static List<OrderStatus> Steps(OrderType type) => type == OrderType.Delivery
        ? new() { OrderStatus.Pending, OrderStatus.Preparing, OrderStatus.Ready, OrderStatus.OutForDelivery, OrderStatus.Completed }
        : new() { OrderStatus.Pending, OrderStatus.Preparing, OrderStatus.Ready, OrderStatus.Completed };

    // The next status the owner can move an order to (null = nothing more to do)
    public static OrderStatus? Next(Order o) => o.Status switch
    {
        OrderStatus.Pending => OrderStatus.Preparing,
        OrderStatus.Preparing => OrderStatus.Ready,
        OrderStatus.Ready => o.OrderType == OrderType.Delivery ? OrderStatus.OutForDelivery : OrderStatus.Completed,
        OrderStatus.OutForDelivery => OrderStatus.Completed,
        _ => null
    };

    // Button text on the owner dashboard
    public static string NextLabel(Order o) => o.Status switch
    {
        OrderStatus.Pending => "Start preparing",
        OrderStatus.Preparing => "Mark Ready",
        OrderStatus.Ready => o.OrderType == OrderType.Delivery ? "Out for delivery" : "Picked up",
        OrderStatus.OutForDelivery => "Delivered",
        _ => ""
    };
}
