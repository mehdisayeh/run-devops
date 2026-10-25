using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using Velora.API.Data;
using Velora.API.Models;
using Velora.Contracts;
using System.Security.Claims;

namespace Velora.API.Services;

public sealed class MongoOrderService : IOrderService
{
    private readonly IMongoCollection<Product> _products;
    private readonly IMongoCollection<StoredOrder> _orders;
    private readonly IHttpContextAccessor? _context;

    public MongoOrderService(IMongoClient client, IMongoCollection<Product> products, IOptions<MongoDbSettings> settings, IHttpContextAccessor? context = null)
    {
        _products = products;
        _context = context;
        _orders = client.GetDatabase(settings.Value.DatabaseName).GetCollection<StoredOrder>("Orders");
    }

    public async Task<CheckoutQuote> QuoteAsync(CartRequest request, CancellationToken cancellationToken)
    {
        var items = CheckoutPricing.Normalize(request);
        var ids = items.Select(i => i.ProductId).ToList();
        var products = await _products.Find(Builders<Product>.Filter.In(p => p.Id, ids)).ToListAsync(cancellationToken);
        return CheckoutPricing.Quote(items, products);
    }

    public async Task<OrderReceipt?> GetAsync(string id, CancellationToken cancellationToken)
    {
        var order = await _orders.Find(o => o.Receipt.Id == id).FirstOrDefaultAsync(cancellationToken);
        if (order?.CustomerId is not null && order.CustomerId != _context?.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier)
            && _context?.HttpContext?.User.IsInRole("Admin") != true) return null;
        return order?.Receipt;
    }

    public async Task<IReadOnlyList<OrderReceipt>> MineAsync(CancellationToken cancellationToken)
    {
        var id = _context?.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new CheckoutException(401, "Sign in to view your orders.");
        return (await _orders.Find(o => o.CustomerId == id).SortByDescending(o => o.Receipt.CreatedAtUtc)
            .Limit(100).ToListAsync(cancellationToken)).Select(o => o.Receipt).ToList();
    }

    public async Task<OrderReceipt> PlaceAsync(PlaceOrderRequest request, CancellationToken cancellationToken)
    {
        var items = CheckoutPricing.Normalize(request);
        var ids = items.Select(i => i.ProductId).ToList();
        var products = await _products.Find(Builders<Product>.Filter.In(p => p.Id, ids)).ToListAsync(cancellationToken);
        var quote = CheckoutPricing.Quote(items, products);
        var receipt = new OrderReceipt
        {
            Id = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32)),
            CreatedAtUtc = DateTime.UtcNow,
            Status = "Placed",
            Address = request.Address,
            Items = quote.Items,
            Total = quote.Total
        };
        var deducted = new List<OrderLine>();
        try
        {
            foreach (var line in quote.Items)
            {
                // Check and decrement together so concurrent orders cannot make stock negative.
                var result = await _products.UpdateOneAsync(
                    p => p.Id == line.ProductId && p.StockQuantity >= line.Quantity,
                    Builders<Product>.Update.Inc(p => p.StockQuantity, -line.Quantity),
                    cancellationToken: cancellationToken);
                if (result.ModifiedCount != 1)
                    throw new CheckoutException(409, $"Not enough stock for {line.Name}.");
                deducted.Add(line);
            }
        }
        catch
        {
            await RestoreStockAsync(deducted);
            throw;
        }

        try
        {
            await _orders.InsertOneAsync(new StoredOrder
            {
                IdempotencyKey = Guid.NewGuid().ToString("N"), Receipt = receipt,
                CustomerId = _context?.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier)
            }, cancellationToken: cancellationToken);
        }
        catch (MongoWriteException)
        {
            // A rejected insert did not save the order. Return its stock.
            await RestoreStockAsync(deducted);
            throw;
        }
        // A network failure can have an unknown outcome: do not automatically retry it.
        return receipt;
    }

    private async Task RestoreStockAsync(IEnumerable<OrderLine> lines)
    {
        foreach (var line in lines)
            await _products.UpdateOneAsync(p => p.Id == line.ProductId,
                Builders<Product>.Update.Inc(p => p.StockQuantity, line.Quantity),
                cancellationToken: CancellationToken.None);
    }
}
