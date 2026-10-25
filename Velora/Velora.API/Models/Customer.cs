using MongoDB.Bson.Serialization.Attributes;
using Velora.Contracts;

namespace Velora.API.Models;

public sealed class Customer
{
    [BsonId] public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string FullName { get; set; } = "";
    public string Email { get; set; } = "";
    public string NormalizedEmail { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string[] Roles { get; set; } = ["Customer"];
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public List<SavedAddress> Addresses { get; set; } = [];
    public int FailedLoginCount { get; set; }
    public DateTime? LockedUntilUtc { get; set; }
    public CustomerProfile Profile() => new(Id, FullName, Email, Roles, CreatedAtUtc, Addresses);
}

public sealed class CustomerSession
{
    [BsonId] public string TokenHash { get; set; } = "";
    public string CustomerId { get; set; } = "";
    public DateTime ExpiresAtUtc { get; set; }
}
