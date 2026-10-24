using System.Security.Cryptography;
using System.Text.Json;
using Velora.API.Models;
using Velora.Contracts;

namespace Velora.API.Services;

public sealed class CheckoutException(int statusCode, string message, Exception? innerException = null) : Exception(message, innerException)
{
    public int StatusCode { get; } = statusCode;
}

public static class CheckoutPricing
{
    public static List<CartLineRequest> Normalize(CartRequest request)
    {
        if (request.Items is null || request.Items.Count is < 1 or > 50 ||
            request.Items.Any(i => i is null || i.ProductId < 1 || i.Quantity is < 1 or > 100))
            throw new CheckoutException(400, "Choose between 1 and 50 products, with quantities from 1 to 100.");
        if (request.Items.Select(i => i.ProductId).Distinct().Count() != request.Items.Count)
            throw new CheckoutException(400, "Each product must appear only once in the bag.");
        return request.Items.OrderBy(i => i.ProductId).ToList();
    }

    public static CheckoutQuote Quote(List<CartLineRequest> items, IReadOnlyList<Product> products)
    {
        var lines = new List<OrderLine>();
        foreach (var item in items)
        {
            var product = products.SingleOrDefault(p => p.Id == item.ProductId);
            if (product is null) throw new CheckoutException(409, $"Product {item.ProductId} is no longer available. Update your bag.");
            if (product.StockQuantity < item.Quantity)
                throw new CheckoutException(409, $"Only {product.StockQuantity} of {product.Name} are available. Update your bag.");
            if (product.Currency != "EUR" || product.Price <= 0 || product.Price != decimal.Round(product.Price, 2))
                throw new CheckoutException(409, $"{product.Name} cannot currently be ordered.");
            lines.Add(new(product.Id, product.Sku, product.Name, item.Quantity, product.Price));
        }
        return new(lines, lines.Sum(i => i.LineTotal), "EUR", Hash(lines));
    }

    public static string Hash<T>(T value) => Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value)));

    public static string Fingerprint(PlaceOrderRequest request, List<CartLineRequest> items) =>
        Hash(new { Items = items, request.Address, request.ExpectedTotal, request.QuoteVersion });

    public static OrderReceipt Replay(StoredOrder existing, string fingerprint)
    {
        if (existing.Fingerprint != fingerprint)
            throw new CheckoutException(409, "This submission key was already used for a different order. Start a new checkout.");
        return existing.Receipt;
    }
}
