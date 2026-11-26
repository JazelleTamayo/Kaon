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
        OrderStatus.OutForDelivery => string.IsNullOrWhiteSpace(o.RiderName)
            ? "Your order is on its way."
            : "Your order is on its way with " + o.RiderName + ".",
        OrderStatus.Cancelled => string.IsNullOrWhiteSpace(o.CancelReason)
            ? "This order was cancelled by the carinderia. Please contact them for details."
            : "This order was cancelled by the carinderia. Reason: " + o.CancelReason,
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

    // Quick choices when the owner cancels an order (plus "Other" for their own words)
    public static readonly string[] CancelReasons =
    {
        "Sold out",
        "Too many orders right now",
        "We can't deliver to this address",
        "Customer asked to cancel",
        "Store is closing"
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

    // What the order tracker bar needs to know about an order
    public static object Snap(Order o)
    {
        var steps = Steps(o.OrderType);
        return new
        {
            id = o.Id,
            status = o.Status.ToString(),
            label = Label(o.Status),
            message = Message(o),
            type = o.OrderType == OrderType.Delivery ? "Delivery" : "Pickup",
            step = steps.IndexOf(o.Status),
            steps = steps.Select(Label).ToArray()
        };
    }

    public static string PayMethodLabel(Order o) => o.PaymentMethod == PaymentMethod.Cash
        ? (o.OrderType == OrderType.Delivery ? "Cash on delivery" : "Cash at pickup")
        : "GCash / cashless";

    public static string PayStatusLabel(Order o) => o.PaymentStatus switch
    {
        PaymentStatus.Paid => "Paid",
        PaymentStatus.DepositPaid => "Deposit paid, balance due",
        PaymentStatus.ProofSent => "Proof sent",
        _ => o.DepositAmount != null
            ? (o.PaymentMethod == PaymentMethod.Cash ? "Deposit due in person" : "Waiting for deposit")
            : o.PaymentMethod == PaymentMethod.Cash ? "Pay in person" : "Waiting for payment"
    };

    // Owner dashboard: the "Mark paid" button's text, which changes once a deposit has been collected
    public static string MarkPaidLabel(Order o) => o.DepositAmount == null
        ? "Mark paid"
        : o.PaymentStatus == PaymentStatus.DepositPaid ? "Mark fully paid" : "Confirm deposit";

    // Module 7: what is still to be collected in person when the order is handed over
    public static decimal ToCollect(Order o) => o.PaymentStatus switch
    {
        PaymentStatus.Paid => 0,
        PaymentStatus.DepositPaid => o.Total - (o.DepositAmount ?? 0),
        _ => o.Total
    };

    // A short random code for a rider's private link (128 bits, cannot be guessed)
    public static string NewRiderToken() =>
        Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
