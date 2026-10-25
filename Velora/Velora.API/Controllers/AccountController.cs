using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using MongoDB.Driver;
using Velora.API.Services;
using Velora.Contracts;

namespace Velora.API.Controllers;

[ApiController, Route("api/account"), Authorize]
[RequestSizeLimit(16384), ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AccountController(AccountService accounts, ILogger<AccountController> logger) : ControllerBase
{
    private string CustomerId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;
    [HttpPost("register"), AllowAnonymous, EnableRateLimiting("account-login")]
    public Task<ActionResult<CustomerProfile>> Register(RegisterRequest request, CancellationToken ct) => Run(() => accounts.RegisterAsync(request, ct));
    [HttpPost("login"), AllowAnonymous, EnableRateLimiting("account-login")]
    public Task<ActionResult<AccountSession>> Login(LoginRequest request, CancellationToken ct) => Run(() => accounts.LoginAsync(request, ct));
    [HttpGet("me")]
    public Task<ActionResult<CustomerProfile>> Me(CancellationToken ct) => Run(() => accounts.ProfileAsync(CustomerId, ct));
    [HttpPut("me")]
    public Task<ActionResult<CustomerProfile>> Update(ProfileRequest request, CancellationToken ct) => Run(() => accounts.UpdateAsync(CustomerId, request, ct));
    [HttpPost("logout")]
    public Task<ActionResult<bool>> Logout(CancellationToken ct) => Run(async () =>
    { await accounts.LogoutAsync(Request.Headers.Authorization.ToString()[7..], ct); return true; });
    [HttpPost("addresses")]
    public Task<ActionResult<SavedAddress>> AddAddress(ShippingAddress request, CancellationToken ct) => Run(() => accounts.SaveAddressAsync(CustomerId, request, null, ct));
    [HttpPut("addresses/{id}")]
    public Task<ActionResult<SavedAddress>> EditAddress(string id, ShippingAddress request, CancellationToken ct) => Run(() => accounts.SaveAddressAsync(CustomerId, request, id, ct));
    [HttpDelete("addresses/{id}")]
    public Task<ActionResult<bool>> DeleteAddress(string id, CancellationToken ct) => Run(async () =>
    { await accounts.DeleteAddressAsync(CustomerId, id, ct); return true; });
    [HttpGet("customers"), Authorize(Roles = "Admin")]
    public Task<ActionResult<List<CustomerSummary>>> Customers(CancellationToken ct) => Run(() => accounts.CustomersAsync(ct));

    private async Task<ActionResult<T>> Run<T>(Func<Task<T>> action)
    {
        try { return Ok(await action()); }
        catch (CheckoutException ex) { return Problem(statusCode: ex.StatusCode, detail: ex.Message); }
        catch (Exception ex) when (ex is MongoException or TimeoutException)
        {
            logger.LogError(ex, "Account storage request failed");
            return Problem(statusCode: 503, detail: "Accounts are temporarily unavailable. Please try again later.");
        }
    }
}
