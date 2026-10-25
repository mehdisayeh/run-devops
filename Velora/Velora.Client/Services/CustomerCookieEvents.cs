using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Velora.Contracts;

namespace Velora.Client.Services;

public sealed class CustomerCookieEvents(AccountApiClient accounts) : CookieAuthenticationEvents
{
    public static ClaimsPrincipal Principal(CustomerProfile profile) => new(new ClaimsIdentity(
        new[] { new Claim(ClaimTypes.NameIdentifier, profile.Id), new Claim(ClaimTypes.Name, profile.FullName), new Claim(ClaimTypes.Email, profile.Email) }
            .Concat(profile.Roles.Select(role => new Claim(ClaimTypes.Role, role))), CookieAuthenticationDefaults.AuthenticationScheme));

    public override async Task ValidatePrincipal(CookieValidatePrincipalContext context)
    {
        var token = context.Properties.GetTokenValue("access_token");
        if (string.IsNullOrEmpty(token)) { context.RejectPrincipal(); return; }
        try
        {
            var profile = await accounts.RequestAsync<CustomerProfile>(HttpMethod.Get, "api/account/me", null, context.HttpContext.RequestAborted, token);
            context.ReplacePrincipal(Principal(profile));
        }
        catch (AccountApiException ex)
        {
            context.RejectPrincipal();
            if (ex.StatusCode != 401) context.HttpContext.Items["AccountUnavailable"] = true;
        }
    }
}
