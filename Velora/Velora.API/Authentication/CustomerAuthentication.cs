using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using Velora.API.Services;

namespace Velora.API.Authentication;

public sealed class CustomerAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger, UrlEncoder encoder, AccountService accounts)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.ToString();
        if (string.IsNullOrEmpty(header)) return AuthenticateResult.NoResult();
        if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return AuthenticateResult.Fail("Bearer token required.");
        try
        {
            var customer = await accounts.AuthenticateAsync(header[7..], Context.RequestAborted);
            if (customer is null) return AuthenticateResult.Fail("Invalid or expired session.");
            var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, customer.Id), new(ClaimTypes.Name, customer.FullName), new(ClaimTypes.Email, customer.Email) };
            claims.AddRange(customer.Roles.Select(role => new Claim(ClaimTypes.Role, role)));
            return AuthenticateResult.Success(new(new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name)), Scheme.Name));
        }
        catch (Exception ex) when (ex is MongoException or TimeoutException)
        {
            Context.Items["AccountStorageUnavailable"] = true;
            Logger.LogError(ex, "Account authentication storage unavailable");
            return AuthenticateResult.Fail("Account service unavailable.");
        }
    }

    protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        if (Context.Items.ContainsKey("AccountStorageUnavailable"))
        {
            Response.StatusCode = 503;
            await Response.WriteAsJsonAsync(new { detail = "Accounts are temporarily unavailable. Please try again later." });
        }
        else { Response.StatusCode = 401; Response.Headers.WWWAuthenticate = "Bearer"; }
    }
}
