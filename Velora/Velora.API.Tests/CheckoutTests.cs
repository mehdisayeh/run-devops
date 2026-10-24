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
    public void AddressRequiresFieldsAndValidEmail()
    {
        var address = new ShippingAddress { Email = "invalid" };
        List<ValidationResult> failures = [];
        Assert.False(Validator.TryValidateObject(address, new(address), failures, true));
        Assert.Contains(failures, f => f.MemberNames.Contains("Email"));
        Assert.Contains(failures, f => f.MemberNames.Contains("Line1"));
    }
}
