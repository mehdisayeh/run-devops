using System.ComponentModel.DataAnnotations;

namespace Velora.Contracts;

public class CartRequest
{
    [Required, MinLength(1), MaxLength(50)]
    public List<CartLineRequest> Items { get; set; } = [];
}

public sealed class CartLineRequest
{
    [Range(1, int.MaxValue)] public int ProductId { get; set; }
    [Range(1, 100)] public int Quantity { get; set; }
}

public sealed class ShippingAddress
{
    [Required, StringLength(100)] public string FullName { get; set; } = "";
    [Required, EmailAddress, StringLength(254)] public string Email { get; set; } = "";
    [Required, StringLength(200)] public string Line1 { get; set; } = "";
    [StringLength(200)] public string? Line2 { get; set; }
    [Required, StringLength(100)] public string City { get; set; } = "";
    [Required, StringLength(30)] public string PostalCode { get; set; } = "";
    [Required, StringLength(100)] public string Country { get; set; } = "";
}

public sealed class PlaceOrderRequest : CartRequest
{
    [Required] public ShippingAddress Address { get; set; } = new();

}

public sealed record OrderLine(int ProductId, string Sku, string Name, int Quantity, decimal UnitPrice)
{
    public decimal LineTotal => UnitPrice * Quantity;
}

public sealed record CheckoutQuote(List<OrderLine> Items, decimal Total, string Currency);

public sealed class OrderReceipt
{
    public string Id { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; }
    public string Status { get; set; } = "Placed";
    public string PaymentStatus { get; set; } = "Unpaid";
    public ShippingAddress Address { get; set; } = new();
    public List<OrderLine> Items { get; set; } = [];
    public decimal Total { get; set; }
    public string Currency { get; set; } = "EUR";
}
