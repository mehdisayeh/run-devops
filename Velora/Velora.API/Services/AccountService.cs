using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using Velora.API.Data;
using Velora.API.Models;
using Velora.Contracts;

namespace Velora.API.Services;

public sealed class AccountService
{
    private readonly IMongoCollection<Customer> _customers;
    private readonly IMongoCollection<CustomerSession> _sessions;
    private readonly IPasswordHasher<Customer> _hasher;
    private static readonly Lazy<string> DummyHash = new(() => new PasswordHasher<Customer>(
        Options.Create(new PasswordHasherOptions { IterationCount = 210000 })).HashPassword(new(), "unused-dummy-password"));

    public AccountService(IMongoClient client, IOptions<MongoDbSettings> settings, IPasswordHasher<Customer> hasher)
    {
        var database = client.GetDatabase(settings.Value.DatabaseName);
        _customers = database.GetCollection<Customer>("Customers");
        _sessions = database.GetCollection<CustomerSession>("CustomerSessions");
        _hasher = hasher;
    }

    public static string NormalizeEmail(string email) => email.Trim().ToUpperInvariant();
    public static string TokenHash(string token) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    public async Task<CustomerProfile> RegisterAsync(RegisterRequest request, CancellationToken ct)
    {
        var customer = new Customer { FullName = request.FullName.Trim(), Email = request.Email.Trim(), NormalizedEmail = NormalizeEmail(request.Email) };
        customer.PasswordHash = _hasher.HashPassword(customer, request.Password);
        try { await _customers.InsertOneAsync(customer, cancellationToken: ct); }
        catch (MongoWriteException ex) when (ex.WriteError.Category == ServerErrorCategory.DuplicateKey)
        { throw new CheckoutException(409, "An account with this email already exists. Sign in instead."); }
        return customer.Profile();
    }

    public async Task<AccountSession> LoginAsync(LoginRequest request, CancellationToken ct)
    {
        var email = NormalizeEmail(request.Email);
        var customer = await _customers.Find(c => c.NormalizedEmail == email).FirstOrDefaultAsync(ct);
        var now = DateTime.UtcNow;
        var result = _hasher.VerifyHashedPassword(customer ?? new(), customer?.PasswordHash ?? DummyHash.Value, request.Password);
        if (customer is null || customer.LockedUntilUtc > now)
            throw new CheckoutException(401, "Email or password is incorrect, or sign-in is temporarily locked. Try again later.");
        if (customer.LockedUntilUtc is not null)
            await _customers.UpdateOneAsync(c => c.Id == customer.Id && c.LockedUntilUtc <= now,
                Builders<Customer>.Update.Set(c => c.FailedLoginCount, 0).Set(c => c.LockedUntilUtc, null), cancellationToken: ct);
        if (result == PasswordVerificationResult.Failed)
        {
            var failed = await _customers.FindOneAndUpdateAsync(c => c.Id == customer.Id,
                Builders<Customer>.Update.Inc(c => c.FailedLoginCount, 1),
                new FindOneAndUpdateOptions<Customer> { ReturnDocument = ReturnDocument.After }, ct);
            if (failed.FailedLoginCount >= 5)
                await _customers.UpdateOneAsync(c => c.Id == customer.Id,
                    Builders<Customer>.Update.Set(c => c.LockedUntilUtc, now.AddMinutes(15)), cancellationToken: ct);
            throw new CheckoutException(401, "Email or password is incorrect, or sign-in is temporarily locked. Try again later.");
        }
        var update = Builders<Customer>.Update.Set(c => c.FailedLoginCount, 0).Set(c => c.LockedUntilUtc, null);
        if (result == PasswordVerificationResult.SuccessRehashNeeded)
            update = update.Set(c => c.PasswordHash, _hasher.HashPassword(customer, request.Password));
        await _customers.UpdateOneAsync(c => c.Id == customer.Id, update, cancellationToken: ct);
        var token = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
        var expiry = now.AddHours(8);
        await _sessions.InsertOneAsync(new() { TokenHash = TokenHash(token), CustomerId = customer.Id, ExpiresAtUtc = expiry }, cancellationToken: ct);
        return new(token, expiry, customer.Profile());
    }

    public async Task<Customer?> AuthenticateAsync(string token, CancellationToken ct)
    {
        if (token.Length != 64 || !token.All(Uri.IsHexDigit)) return null;
        var hash = TokenHash(token);
        var session = await _sessions.Find(s => s.TokenHash == hash && s.ExpiresAtUtc > DateTime.UtcNow).FirstOrDefaultAsync(ct);
        return session is null ? null : await _customers.Find(c => c.Id == session.CustomerId).FirstOrDefaultAsync(ct);
    }

    public Task LogoutAsync(string token, CancellationToken ct) =>
        _sessions.DeleteOneAsync(s => s.TokenHash == TokenHash(token), ct);

    public async Task<CustomerProfile> ProfileAsync(string id, CancellationToken ct) =>
        (await _customers.Find(c => c.Id == id).FirstOrDefaultAsync(ct))?.Profile() ?? throw new CheckoutException(404, "Account not found.");

    public async Task<CustomerProfile> UpdateAsync(string id, ProfileRequest request, CancellationToken ct)
    {
        var customer = await _customers.FindOneAndUpdateAsync(c => c.Id == id,
            Builders<Customer>.Update.Set(c => c.FullName, request.FullName.Trim()),
            new FindOneAndUpdateOptions<Customer> { ReturnDocument = ReturnDocument.After }, ct);
        return customer?.Profile() ?? throw new CheckoutException(404, "Account not found.");
    }

    public async Task<SavedAddress> SaveAddressAsync(string id, ShippingAddress address, string? addressId, CancellationToken ct)
    {
        var saved = new SavedAddress(addressId ?? Guid.NewGuid().ToString("N"), address);
        var filter = Builders<Customer>.Filter.Eq(c => c.Id, id);
        UpdateDefinition<Customer> update;
        if (addressId is null)
        {
            filter &= Builders<Customer>.Filter.SizeLt(c => c.Addresses, 10);
            update = Builders<Customer>.Update.Push(c => c.Addresses, saved);
        }
        else
        {
            filter &= Builders<Customer>.Filter.ElemMatch(c => c.Addresses, a => a.Id == addressId);
            update = Builders<Customer>.Update.Set("Addresses.$", saved);
        }
        var result = await _customers.UpdateOneAsync(filter, update, cancellationToken: ct);
        if (result.MatchedCount != 1) throw new CheckoutException(addressId is null ? 409 : 404,
            addressId is null ? "You can save up to 10 addresses. Remove one before adding another." : "Address not found.");
        return saved;
    }

    public async Task DeleteAddressAsync(string id, string addressId, CancellationToken ct)
    {
        var result = await _customers.UpdateOneAsync(c => c.Id == id,
            Builders<Customer>.Update.PullFilter(c => c.Addresses, a => a.Id == addressId), cancellationToken: ct);
        if (result.ModifiedCount == 0) throw new CheckoutException(404, "Address not found.");
    }

    public async Task<List<CustomerSummary>> CustomersAsync(CancellationToken ct) =>
        await _customers.Find(FilterDefinition<Customer>.Empty).SortByDescending(c => c.CreatedAtUtc).Limit(100)
            .Project(c => new CustomerSummary(c.Id, c.FullName, c.Email, c.Roles, c.CreatedAtUtc)).ToListAsync(ct);
}
