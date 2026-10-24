using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using Velora.API.Data;
using Velora.API.Models;
using Velora.Contracts;

namespace Velora.API.Services;

public sealed class MongoOrderService : IOrderService
{
    private readonly IMongoClient _client;
    private readonly IMongoCollection<Product> _products;
    private readonly IMongoCollection<StoredOrder> _orders;

    public MongoOrderService(IMongoClient client, IMongoCollection<Product> products, IOptions<MongoDbSettings> settings)
    {
        _client = client;
        _products = products;
        _orders = client.GetDatabase(settings.Value.DatabaseName).GetCollection<StoredOrder>("Orders");
    }

    public async Task<CheckoutQuote> QuoteAsync(CartRequest request, CancellationToken cancellationToken)
    {
        var items = CheckoutPricing.Normalize(request);
        var ids = items.Select(i => i.ProductId).ToList();
        var products = await _products.Find(Builders<Product>.Filter.In(p => p.Id, ids)).ToListAsync(cancellationToken);
        return CheckoutPricing.Quote(items, products);
    }

    public async Task<OrderReceipt?> GetAsync(string id, CancellationToken cancellationToken) =>
        (await _orders.Find(o => o.Receipt.Id == id).FirstOrDefaultAsync(cancellationToken))?.Receipt;

    public async Task<OrderReceipt> PlaceAsync(PlaceOrderRequest request, CancellationToken cancellationToken)
    {
        var items = CheckoutPricing.Normalize(request);
        var fingerprint = CheckoutPricing.Fingerprint(request, items);
        var key = request.IdempotencyKey.ToLowerInvariant();
        // Check committed orders before stock: a retry must work even after selling the last item.
        var existing = await _orders.Find(o => o.IdempotencyKey == key).FirstOrDefaultAsync(cancellationToken);
        if (existing is not null) return CheckoutPricing.Replay(existing, fingerprint);

        using var session = await _client.StartSessionAsync(cancellationToken: cancellationToken);
        try
        {
            return await session.WithTransactionAsync(async (transaction, ct) =>
            {
                var prior = await _orders.Find(transaction, o => o.IdempotencyKey == key).FirstOrDefaultAsync(ct);
                if (prior is not null) return CheckoutPricing.Replay(prior, fingerprint);
                var ids = items.Select(i => i.ProductId).ToList();
                var products = await _products.Find(transaction, Builders<Product>.Filter.In(p => p.Id, ids)).ToListAsync(ct);
                var quote = CheckoutPricing.Quote(items, products);
                if (quote.Total != request.ExpectedTotal || quote.Version != request.QuoteVersion)
                    throw new CheckoutException(409, "Product details or prices have changed. Review a fresh order summary before submitting.");

                var receipt = new OrderReceipt
                {
                    Id = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32)),
                    CreatedAtUtc = DateTime.UtcNow,
                    Address = request.Address,
                    Items = quote.Items,
                    Total = quote.Total
                };
                await _orders.InsertOneAsync(transaction, new StoredOrder
                {
                    IdempotencyKey = key, Fingerprint = fingerprint, Receipt = receipt
                }, cancellationToken: ct);
                foreach (var line in quote.Items)
                {
                    var result = await _products.UpdateOneAsync(transaction,
                        p => p.Id == line.ProductId && p.StockQuantity >= line.Quantity,
                        Builders<Product>.Update.Inc(p => p.StockQuantity, -line.Quantity), cancellationToken: ct);
                    if (result.ModifiedCount != 1)
                        throw new CheckoutException(409, "Stock changed while placing your order. Review your bag and try again.");
                }
                return receipt;
            }, new TransactionOptions(readConcern: ReadConcern.Snapshot, writeConcern: WriteConcern.WMajority), cancellationToken);
        }
        catch (MongoWriteException ex) when (ex.WriteError.Category == ServerErrorCategory.DuplicateKey)
        {
            var committed = await _orders.Find(o => o.IdempotencyKey == key).FirstOrDefaultAsync(cancellationToken);
            if (committed is not null) return CheckoutPricing.Replay(committed, fingerprint);
            throw;
        }
        catch (Exception ex) when (ex is NotSupportedException || ex is MongoCommandException { Code: 20 })
        {
            throw new CheckoutException(503, "Checkout is temporarily unavailable. No order was placed. Please try again later.", ex);
        }
    }
}
