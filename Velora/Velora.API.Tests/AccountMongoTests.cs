using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using Velora.API.Data;
using Velora.API.Models;
using Velora.API.Services;
using Velora.Contracts;

namespace Velora.API.Tests;

public sealed class AccountMongoFactAttribute : FactAttribute
{
    public AccountMongoFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("VELORA_ACCOUNT_TEST_MONGO")))
            Skip = "Set VELORA_ACCOUNT_TEST_MONGO to run isolated account database tests.";
    }
}

public class AccountMongoTests
{
    private const string Password = "A long account test passphrase";
    private static async Task WithStore(Func<AccountService, IMongoDatabase, IMongoClient, IOptions<MongoDbSettings>, Task> test)
    {
        var client = new MongoClient(Environment.GetEnvironmentVariable("VELORA_ACCOUNT_TEST_MONGO"));
        var name = "velora_accounts_test_" + Guid.NewGuid().ToString("N");
        var settings = Options.Create(new MongoDbSettings { DatabaseName = name });
        var db = client.GetDatabase(name);
        try
        {
            await new AccountIndexes(client, settings).StartAsync(default);
            var hasher = new PasswordHasher<Customer>(Options.Create(new PasswordHasherOptions { IterationCount = 210000 }));
            await test(new AccountService(client, settings, hasher), db, client, settings);
        }
        finally { await client.DropDatabaseAsync(name); }
    }

    private static RegisterRequest Registration(string email = "guest@example.test") =>
        new() { FullName = "Test Guest", Email = email, Password = Password };
    private static ShippingAddress Address() => new()
    { FullName = "Test Guest", Email = "guest@example.test", Line1 = "1 Test Street", City = "Paris", PostalCode = "75001", Country = "France" };

    [AccountMongoFact]
    public Task RegistrationUsesUniqueNormalizedEmailAndNeverReturnsPasswordHash() => WithStore(async (accounts, db, _, _) =>
    {
        var customer = await accounts.RegisterAsync(Registration("Guest@Example.Test"), default);
        Assert.Equal(new[] { "Customer" }, customer.Roles);
        var stored = await db.GetCollection<Customer>("Customers").Find(c => c.Id == customer.Id).SingleAsync();
        Assert.Equal("GUEST@EXAMPLE.TEST", stored.NormalizedEmail);
        Assert.NotEqual(Password, stored.PasswordHash);
        Assert.DoesNotContain("Password", JsonSerializer.Serialize(customer));
        Assert.Equal(409, (await Assert.ThrowsAsync<CheckoutException>(() => accounts.RegisterAsync(Registration(), default))).StatusCode);
    });

    [AccountMongoFact]
    public Task SessionsAreHashedRevocableAndExpiryIsEnforcedWithoutWaitingForTtlDeletion() => WithStore(async (accounts, db, _, _) =>
    {
        await accounts.RegisterAsync(Registration(), default);
        var login = await accounts.LoginAsync(new() { Email = "GUEST@example.test", Password = Password }, default);
        Assert.NotNull(await accounts.AuthenticateAsync(login.AccessToken, default));
        var sessions = db.GetCollection<CustomerSession>("CustomerSessions");
        var stored = await sessions.Find(FilterDefinition<CustomerSession>.Empty).SingleAsync();
        Assert.NotEqual(login.AccessToken, stored.TokenHash);
        await accounts.LogoutAsync(login.AccessToken, default);
        Assert.Null(await accounts.AuthenticateAsync(login.AccessToken, default));
        login = await accounts.LoginAsync(new() { Email = "guest@example.test", Password = Password }, default);
        await sessions.UpdateManyAsync(FilterDefinition<CustomerSession>.Empty,
            Builders<CustomerSession>.Update.Set(s => s.ExpiresAtUtc, DateTime.UtcNow.AddMinutes(-1)));
        Assert.Null(await accounts.AuthenticateAsync(login.AccessToken, default));
        Assert.Null(await accounts.AuthenticateAsync("invalid", default));
    });

    [AccountMongoFact]
    public Task RepeatedBadPasswordsLockTheAccountAndExpiredLockoutAllowsSignIn() => WithStore(async (accounts, db, _, _) =>
    {
        var customer = await accounts.RegisterAsync(Registration(), default);
        for (var i = 0; i < 5; i++)
            Assert.Equal(401, (await Assert.ThrowsAsync<CheckoutException>(() => accounts.LoginAsync(new()
            { Email = customer.Email, Password = "incorrect" }, default))).StatusCode);
        await Assert.ThrowsAsync<CheckoutException>(() => accounts.LoginAsync(new() { Email = customer.Email, Password = Password }, default));
        await db.GetCollection<Customer>("Customers").UpdateOneAsync(c => c.Id == customer.Id,
            Builders<Customer>.Update.Set(c => c.LockedUntilUtc, DateTime.UtcNow.AddSeconds(-1)));
        Assert.NotNull(await accounts.LoginAsync(new() { Email = customer.Email, Password = Password }, default));
    });

    [AccountMongoFact]
    public Task ProfilesAndAddressesAreScopedToTheCustomerAndAddressCountIsBounded() => WithStore(async (accounts, _, _, _) =>
    {
        var first = await accounts.RegisterAsync(Registration(), default);
        var second = await accounts.RegisterAsync(Registration("second@example.test"), default);
        var saved = await accounts.SaveAddressAsync(first.Id, Address(), null, default);
        Assert.Empty((await accounts.ProfileAsync(second.Id, default)).Addresses);
        await Assert.ThrowsAsync<CheckoutException>(() => accounts.SaveAddressAsync(second.Id, Address(), saved.Id, default));
        await Assert.ThrowsAsync<CheckoutException>(() => accounts.DeleteAddressAsync(second.Id, saved.Id, default));
        var address = Address(); address.City = "Lyon";
        await accounts.SaveAddressAsync(first.Id, address, saved.Id, default);
        Assert.Equal("Lyon", (await accounts.ProfileAsync(first.Id, default)).Addresses.Single().Address.City);
        await accounts.UpdateAsync(first.Id, new() { FullName = "New name" }, default);
        Assert.Equal("New name", (await accounts.ProfileAsync(first.Id, default)).FullName);
        for (var i = 1; i < 10; i++) await accounts.SaveAddressAsync(first.Id, Address(), null, default);
        await Assert.ThrowsAsync<CheckoutException>(() => accounts.SaveAddressAsync(first.Id, Address(), null, default));
        await accounts.DeleteAddressAsync(first.Id, saved.Id, default);
        Assert.Equal(9, (await accounts.ProfileAsync(first.Id, default)).Addresses.Count);
    });

    [AccountMongoFact]
    public Task RoleChangesAreRecheckedForExistingSessions() => WithStore(async (accounts, db, _, _) =>
    {
        var profile = await accounts.RegisterAsync(Registration(), default);
        var session = await accounts.LoginAsync(new() { Email = profile.Email, Password = Password }, default);
        await db.GetCollection<Customer>("Customers").UpdateOneAsync(c => c.Id == profile.Id,
            Builders<Customer>.Update.Set(c => c.Roles, new[] { "Customer", "Admin" }));
        Assert.Contains("Admin", (await accounts.AuthenticateAsync(session.AccessToken, default))!.Roles);
        await db.GetCollection<Customer>("Customers").DeleteOneAsync(c => c.Id == profile.Id);
        Assert.Null(await accounts.AuthenticateAsync(session.AccessToken, default));
    });

    [AccountMongoFact]
    public Task AccountOrdersRequireOwnershipOrAdminAndGuestOrdersKeepTheirPrivateLink() => WithStore(async (_, db, client, settings) =>
    {
        var products = db.GetCollection<Product>("Products");
        await products.InsertOneAsync(new() { Id = 1, Price = 10, Name = "Product", StockQuantity = 10 });
        var context = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        void Identity(string? id, string role = "Customer") => context.HttpContext.User = id is null ? new() :
            new ClaimsPrincipal(new ClaimsIdentity([new(ClaimTypes.NameIdentifier, id), new(ClaimTypes.Role, role)], "test"));
        var orders = new MongoOrderService(client, products, settings, context);
        Identity("owner");
        var receipt = await orders.PlaceAsync(new() { Items = [new() { ProductId = 1, Quantity = 1 }], Address = Address() }, default);
        Assert.Single(await orders.MineAsync(default));
        Identity("other");
        Assert.Empty(await orders.MineAsync(default));
        Assert.Null(await orders.GetAsync(receipt.Id, default));
        Identity(null);
        Assert.Null(await orders.GetAsync(receipt.Id, default));
        var guest = await orders.PlaceAsync(new() { Items = [new() { ProductId = 1, Quantity = 1 }], Address = Address() }, default);
        Assert.NotNull(await orders.GetAsync(guest.Id, default));
        Identity("administrator", "Admin");
        Assert.NotNull(await orders.GetAsync(receipt.Id, default));
        Identity("owner");
        Assert.NotNull(await orders.GetAsync(receipt.Id, default));
    });
}
