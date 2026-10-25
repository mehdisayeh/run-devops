using System.ComponentModel.DataAnnotations;

namespace Velora.Contracts;

public sealed class RegisterRequest
{
    [Required, StringLength(100)] public string FullName { get; set; } = "";
    [Required, EmailAddress, StringLength(254)] public string Email { get; set; } = "";
    [Required, StringLength(128, MinimumLength = 12), DataType(DataType.Password)]
    public string Password { get; set; } = "";
}

public sealed class LoginRequest
{
    [Required, EmailAddress, StringLength(254)] public string Email { get; set; } = "";
    [Required, StringLength(128), DataType(DataType.Password)] public string Password { get; set; } = "";
}

public sealed class ProfileRequest
{
    [Required, StringLength(100)] public string FullName { get; set; } = "";
}

public sealed record SavedAddress(string Id, ShippingAddress Address);
public sealed record CustomerProfile(string Id, string FullName, string Email, string[] Roles,
    DateTime CreatedAtUtc, List<SavedAddress> Addresses);
public sealed record AccountSession(string AccessToken, DateTime ExpiresAtUtc, CustomerProfile Customer);
public sealed record CustomerSummary(string Id, string FullName, string Email, string[] Roles, DateTime CreatedAtUtc);
