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
            Skip = "Set VELORA_CHECKOUT_TEST_MONGO to an isolated MongoDB URI (standalone is supported).";
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
        await Task.CompletedTask;
        return new()
        {
            Items = items.ToList(),
            Address = new() { FullName = "Test Guest", Email = "guest@example.test", Line1 = "1 Test Street", City = "Paris", PostalCode = "75001", Country = "France" }
        };
    }

    [MongoCheckoutFact]
    public Task OrderDeductsStockAndSavesReceipt() => WithStore(async (db, service, products) =>
    {
        var request = await Request(service, new CartLineRequest { ProductId = 1, Quantity = 2 });
        var receipt = await service.PlaceAsync(request, default);
        Assert.Equal(3, (await products.Find(p => p.Id == 1).SingleAsync()).StockQuantity);
        Assert.Equal(25m, receipt.Total);
        Assert.Equal("Placed", receipt.Status);
        Assert.Equal(receipt.Total, (await service.GetAsync(receipt.Id, default))!.Total);
        Assert.Equal(1, await db.GetCollection<StoredOrder>("Orders").CountDocumentsAsync(FilterDefinition<StoredOrder>.Empty));
    });

    [MongoCheckoutFact]
    public Task ConcurrentOrdersCannotMakeStockNegative() => WithStore(async (db, service, products) =>
    {
        async Task<bool> Place()
        {
            try { await service.PlaceAsync(await Request(service, new CartLineRequest { ProductId = 2, Quantity = 1 }), default); return true; }
            catch (CheckoutException ex) when (ex.StatusCode == 409) { return false; }
        }
        var outcomes = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Place()));
        Assert.Single(outcomes, success => success);
        Assert.Equal(0, (await products.Find(p => p.Id == 2).SingleAsync()).StockQuantity);
        Assert.Equal(1, await db.GetCollection<StoredOrder>("Orders").CountDocumentsAsync(FilterDefinition<StoredOrder>.Empty));
    });

    [MongoCheckoutFact]
    public Task InsufficientStockDoesNotSaveOrderOrDeductOtherProducts() => WithStore(async (db, service, products) =>
    {
        var request = await Request(service, new CartLineRequest { ProductId = 1, Quantity = 2 }, new() { ProductId = 2, Quantity = 2 });
        await Assert.ThrowsAsync<CheckoutException>(() => service.PlaceAsync(request, default));
        Assert.Equal(5, (await products.Find(p => p.Id == 1).SingleAsync()).StockQuantity);
        Assert.Equal(0, await db.GetCollection<StoredOrder>("Orders").CountDocumentsAsync(FilterDefinition<StoredOrder>.Empty));
    });

    [MongoCheckoutFact]
    public Task RejectedOrderInsertReturnsDeductedStock() => WithStore(async (db, service, products) =>
    {
        await db.RunCommandAsync<BsonDocument>(new BsonDocument
        {
            { "collMod", "Orders" }, { "validator", new BsonDocument("impossible", new BsonDocument("$exists", true)) }
        });
        var request = await Request(service, new CartLineRequest { ProductId = 1, Quantity = 2 });
        await Assert.ThrowsAsync<MongoWriteException>(() => service.PlaceAsync(request, default));
        Assert.Equal(5, (await products.Find(p => p.Id == 1).SingleAsync()).StockQuantity);
    });
}
