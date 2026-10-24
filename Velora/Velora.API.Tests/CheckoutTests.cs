using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using Moq;
using Velora.API.Data;
using Velora.API.Models;
using Velora.API.Services;
using Velora.Contracts;

namespace Velora.API.Tests;

public class CheckoutTests
{
    [Fact]
    public async Task StandaloneDriverRejectionReturnsControlledServiceUnavailable()
    {
        var cursor = new Mock<IAsyncCursor<StoredOrder>>();
        cursor.Setup(c => c.MoveNextAsync(It.IsAny<CancellationToken>())).ReturnsAsync(false);
        var orders = new Mock<IMongoCollection<StoredOrder>>();
        orders.Setup(c => c.FindAsync(It.IsAny<FilterDefinition<StoredOrder>>(),
            It.IsAny<FindOptions<StoredOrder, StoredOrder>>(), It.IsAny<CancellationToken>())).ReturnsAsync(cursor.Object);
        var database = new Mock<IMongoDatabase>();
        database.Setup(d => d.GetCollection<StoredOrder>("Orders", null)).Returns(orders.Object);
        var session = new Mock<IClientSessionHandle>();
        session.Setup(s => s.WithTransactionAsync(
            It.IsAny<Func<IClientSessionHandle, CancellationToken, Task<OrderReceipt>>>(),
            It.IsAny<TransactionOptions>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new NotSupportedException("Standalone servers do not support transactions."));
        var client = new Mock<IMongoClient>();
        client.Setup(c => c.GetDatabase("test", null)).Returns(database.Object);
        client.Setup(c => c.StartSessionAsync(It.IsAny<ClientSessionOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(session.Object);
        var service = new MongoOrderService(client.Object, Mock.Of<IMongoCollection<Product>>(),
            Options.Create(new MongoDbSettings { DatabaseName = "test" }));

        var failure = await Assert.ThrowsAsync<CheckoutException>(() => service.PlaceAsync(new()
        {
            IdempotencyKey = Guid.NewGuid().ToString("N"), Items = [new() { ProductId = 1, Quantity = 1 }]
        }, default));

        Assert.Equal(503, failure.StatusCode);
        Assert.IsType<NotSupportedException>(failure.InnerException);
        Assert.DoesNotContain("Standalone", failure.Message);
    }

    private static Product Product(int id = 1, decimal price = 12.50m, int stock = 3) =>
        new() { Id = id, Name = "Test product", Sku = $"SKU-{id}", Price = price, StockQuantity = stock };

    [Fact]
    public void QuoteUsesCatalogPricesAndQuantities()
    {
        var quote = CheckoutPricing.Quote([new() { ProductId = 1, Quantity = 2 }], [Product()]);
        Assert.Equal(25m, quote.Total);
        Assert.Equal(12.50m, quote.Items.Single().UnitPrice);
        Assert.Equal("EUR", quote.Currency);
    }

    [Theory]
    [InlineData(0)] [InlineData(-1)] [InlineData(101)]
    public void RejectsInvalidQuantities(int quantity) =>
        Assert.Throws<CheckoutException>(() => CheckoutPricing.Normalize(new()
        { Items = [new() { ProductId = 1, Quantity = quantity }] }));

    [Fact]
    public void RejectsEmptyAndDuplicateLines()
    {
        Assert.Throws<CheckoutException>(() => CheckoutPricing.Normalize(new()));
        Assert.Throws<CheckoutException>(() => CheckoutPricing.Normalize(new()
        { Items = [new() { ProductId = 1, Quantity = 1 }, new() { ProductId = 1, Quantity = 1 }] }));
    }

    [Fact]
    public void RejectsMissingProductsAndInsufficientStock()
    {
        List<CartLineRequest> items = [new() { ProductId = 1, Quantity = 2 }];
        Assert.Equal(409, Assert.Throws<CheckoutException>(() => CheckoutPricing.Quote(items, [])).StatusCode);
        Assert.Equal(409, Assert.Throws<CheckoutException>(() => CheckoutPricing.Quote(items, [Product(stock: 1)])).StatusCode);
    }

    [Fact]
    public void QuoteVersionDetectsPriceChangesEvenWhenTotalIsUnchanged()
    {
        List<CartLineRequest> items = [new() { ProductId = 1, Quantity = 1 }, new() { ProductId = 2, Quantity = 1 }];
        var before = CheckoutPricing.Quote(items, [Product(1, 10), Product(2, 20)]);
        var after = CheckoutPricing.Quote(items, [Product(1, 20), Product(2, 10)]);
        Assert.Equal(before.Total, after.Total);
        Assert.NotEqual(before.Version, after.Version);
    }

    [Fact]
    public void ReplayReturnsSameReceiptAndRejectsChangedPayload()
    {
        var stored = new StoredOrder { Fingerprint = "original", Receipt = new() { Id = "receipt" } };
        Assert.Same(stored.Receipt, CheckoutPricing.Replay(stored, "original"));
        Assert.Equal(409, Assert.Throws<CheckoutException>(() => CheckoutPricing.Replay(stored, "changed")).StatusCode);
    }

    [Fact]
    public void AddressRequiresFieldsAndValidEmail()
    {
        var address = new ShippingAddress { Email = "invalid" };
        List<ValidationResult> failures = [];
        Assert.False(Validator.TryValidateObject(address, new(address), failures, true));
        Assert.Contains(failures, f => f.MemberNames.Contains("Email"));
        Assert.Contains(failures, f => f.MemberNames.Contains("Line1"));
    }
}
