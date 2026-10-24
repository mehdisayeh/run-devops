using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using Velora.API.Data;
using Velora.API.Models;
using Velora.API.Services;
using Velora.Contracts;

namespace Velora.API.Tests;

public sealed class MongoCheckoutFactAttribute : FactAttribute
{
    public MongoCheckoutFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("VELORA_CHECKOUT_TEST_MONGO")))
            Skip = "Set VELORA_CHECKOUT_TEST_MONGO to an isolated replica-set MongoDB URI.";
    }
}

public class CheckoutMongoTests
{
    private static async Task WithStore(Func<IMongoDatabase, MongoOrderService, IMongoCollection<Product>, Task> test)
    {
        var client = new MongoClient(Environment.GetEnvironmentVariable("VELORA_CHECKOUT_TEST_MONGO"));
        // Only databases with this newly generated test prefix are ever created/dropped.
        var name = "velora_checkout_test_" + Guid.NewGuid().ToString("N");
        var database = client.GetDatabase(name);
        var products = database.GetCollection<Product>("Products");
        var service = new MongoOrderService(client, products, Options.Create(new MongoDbSettings { DatabaseName = name }));
        try
        {
            await database.CreateCollectionAsync("Orders");
            await products.InsertManyAsync([
                new() { Id = 1, Name = "First", Sku = "ONE", Price = 12.50m, StockQuantity = 5 },
                new() { Id = 2, Name = "Second", Sku = "TWO", Price = 20m, StockQuantity = 1 }
            ]);
            await test(database, service, products);
        }
        finally { await client.DropDatabaseAsync(name); }
    }

    private static async Task<PlaceOrderRequest> Request(MongoOrderService service, params CartLineRequest[] items)
    {
        var quote = await service.QuoteAsync(new() { Items = items.ToList() }, default);
        return new()
        {
            Items = items.ToList(), ExpectedTotal = quote.Total, QuoteVersion = quote.Version,
            IdempotencyKey = Guid.NewGuid().ToString("N"),
            Address = new() { FullName = "Test Guest", Email = "guest@example.test", Line1 = "1 Test Street", City = "Paris", PostalCode = "75001", Country = "France" }
        };
    }

    [MongoCheckoutFact]
    public Task ConcurrentRetriesCreateOneOrderAndDeductStockOnce() => WithStore(async (db, service, products) =>
    {
        var request = await Request(service, new CartLineRequest { ProductId = 2, Quantity = 1 });
        var orders = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => service.PlaceAsync(request, default)));
        Assert.Single(orders.Select(o => o.Id).Distinct());
        Assert.Equal(0, (await products.Find(p => p.Id == 2).SingleAsync()).StockQuantity);
        Assert.Equal(1, await db.GetCollection<StoredOrder>("Orders").CountDocumentsAsync(FilterDefinition<StoredOrder>.Empty));
        Assert.Equal(orders[0].Id, (await service.PlaceAsync(request, default)).Id);
        Assert.Equal(orders[0].Total, (await service.GetAsync(orders[0].Id, default))!.Total);
        request.Address.City = "Lyon";
        Assert.Equal(409, (await Assert.ThrowsAsync<CheckoutException>(() => service.PlaceAsync(request, default))).StatusCode);
    });

    [MongoCheckoutFact]
    public Task CompetingOrdersCannotOversellOrPartiallyDeductAnotherProduct() => WithStore(async (db, service, products) =>
    {
        var first = await Request(service, new CartLineRequest { ProductId = 1, Quantity = 2 }, new() { ProductId = 2, Quantity = 1 });
        var second = await Request(service, new CartLineRequest { ProductId = 1, Quantity = 2 }, new() { ProductId = 2, Quantity = 1 });
        async Task<bool> Place(PlaceOrderRequest request)
        {
            try { await service.PlaceAsync(request, default); return true; }
            catch (CheckoutException ex) when (ex.StatusCode == 409) { return false; }
        }
        var outcomes = await Task.WhenAll(Place(first), Place(second));
        Assert.Single(outcomes, success => success);
        Assert.Equal(3, (await products.Find(p => p.Id == 1).SingleAsync()).StockQuantity);
        Assert.Equal(0, (await products.Find(p => p.Id == 2).SingleAsync()).StockQuantity);
        Assert.Equal(1, await db.GetCollection<StoredOrder>("Orders").CountDocumentsAsync(FilterDefinition<StoredOrder>.Empty));
    });

    [MongoCheckoutFact]
    public Task ChangedPricesAndTamperedTotalsDoNotCreateOrders() => WithStore(async (db, service, products) =>
    {
        var request = await Request(service, new CartLineRequest { ProductId = 1, Quantity = 1 });
        request.ExpectedTotal = 0.01m;
        await Assert.ThrowsAsync<CheckoutException>(() => service.PlaceAsync(request, default));
        request.ExpectedTotal = 12.50m;
        await products.UpdateOneAsync(p => p.Id == 1, Builders<Product>.Update.Set(p => p.Price, 13m));
        await Assert.ThrowsAsync<CheckoutException>(() => service.PlaceAsync(request, default));
        Assert.Equal(5, (await products.Find(p => p.Id == 1).SingleAsync()).StockQuantity);
        Assert.Equal(0, await db.GetCollection<StoredOrder>("Orders").CountDocumentsAsync(FilterDefinition<StoredOrder>.Empty));
    });

    [MongoCheckoutFact]
    public Task DatabaseFailureAfterOrderInsertRollsBackEntireTransaction() => WithStore(async (db, service, products) =>
    {
        var request = await Request(service, new CartLineRequest { ProductId = 1, Quantity = 1 });
        // Force the subsequent inventory write to fail after inserting the order.
        await db.RunCommandAsync<BsonDocument>(new BsonDocument
        {
            { "collMod", "Products" }, { "validator", new BsonDocument("StockQuantity", new BsonDocument("$gte", 5)) }
        });
        await Assert.ThrowsAsync<MongoWriteException>(() => service.PlaceAsync(request, default));
        Assert.Equal(5, (await products.Find(p => p.Id == 1).SingleAsync()).StockQuantity);
        Assert.Equal(0, await db.GetCollection<StoredOrder>("Orders").CountDocumentsAsync(FilterDefinition<StoredOrder>.Empty));
    });
}
