using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Velora.Client.Services;
using Velora.Contracts;

namespace Velora.Client.Controllers;

[Authorize, AutoValidateAntiforgeryToken, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AccountController(AccountApiClient accounts) : Controller
{
    [AllowAnonymous, HttpGet]
    public IActionResult Register() => User.Identity?.IsAuthenticated == true ? RedirectToAction(nameof(Profile)) : View(new RegisterRequest());

    [AllowAnonymous, HttpPost]
    public async Task<IActionResult> Register(RegisterRequest request, CancellationToken ct)
    {
        if (ModelState.IsValid)
        {
            try
            {
                await accounts.RequestAsync<CustomerProfile>(HttpMethod.Post, "api/account/register", request, ct, authenticate: false);
                TempData["AccountMessage"] = "Your account is ready. Sign in to continue.";
                return RedirectToAction(nameof(Login));
            }
            catch (AccountApiException ex) { ModelState.AddModelError("", ex.Message); }
        }
        ModelState.SetModelValue(nameof(request.Password), "", ""); request.Password = "";
        return View(request);
    }

    [AllowAnonymous, HttpGet]
    public IActionResult Login(string? returnUrl)
    {
        if (User.Identity?.IsAuthenticated == true) return RedirectToAction(nameof(Profile));
        ViewData["ReturnUrl"] = Url.IsLocalUrl(returnUrl) ? returnUrl : null;
        return View(new LoginRequest());
    }

    [AllowAnonymous, HttpPost]
    public async Task<IActionResult> Login(LoginRequest request, string? returnUrl, CancellationToken ct)
    {
        ViewData["ReturnUrl"] = Url.IsLocalUrl(returnUrl) ? returnUrl : null;
        if (ModelState.IsValid)
        {
            try
            {
                var session = await accounts.RequestAsync<AccountSession>(HttpMethod.Post, "api/account/login", request, ct, authenticate: false);
                var properties = new AuthenticationProperties { ExpiresUtc = new DateTimeOffset(session.ExpiresAtUtc), IsPersistent = false };
                properties.StoreTokens([new AuthenticationToken { Name = "access_token", Value = session.AccessToken }]);
                await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, CustomerCookieEvents.Principal(session.Customer), properties);
                return Url.IsLocalUrl(returnUrl) ? LocalRedirect(returnUrl!) : RedirectToAction(nameof(Profile));
            }
            catch (AccountApiException ex) { ModelState.AddModelError("", ex.Message); }
        }
        ModelState.SetModelValue(nameof(request.Password), "", ""); request.Password = "";
        return View(request);
    }

    [HttpPost]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        try { await accounts.RequestAsync<bool>(HttpMethod.Post, "api/account/logout", null, ct); }
        catch (AccountApiException) { TempData["AccountMessage"] = "Signed out on this device. Server session revocation could not be confirmed."; }
        await HttpContext.SignOutAsync();
        return RedirectToAction(nameof(Login));
    }

    [HttpGet]
    public async Task<IActionResult> Profile(CancellationToken ct)
    {
        try { return View(await accounts.RequestAsync<CustomerProfile>(HttpMethod.Get, "api/account/me", null, ct)); }
        catch (AccountApiException ex) { return Unavailable(ex); }
    }

    [HttpPost]
    public async Task<IActionResult> UpdateProfile(ProfileRequest request, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            TempData["AccountMessage"] = "Enter a name between 1 and 100 characters.";
            return RedirectToAction(nameof(Profile));
        }
        try
        {
            await accounts.RequestAsync<CustomerProfile>(HttpMethod.Put, "api/account/me", request, ct);
            TempData["AccountMessage"] = "Profile updated.";
            return RedirectToAction(nameof(Profile));
        }
        catch (AccountApiException ex) { return Unavailable(ex); }
    }

    [HttpGet]
    public async Task<IActionResult> Address(string? id, CancellationToken ct)
    {
        ViewData["AddressId"] = id;
        if (id is null) return View(new ShippingAddress { FullName = User.Identity?.Name ?? "", Email = User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value ?? "" });
        try
        {
            var profile = await accounts.RequestAsync<CustomerProfile>(HttpMethod.Get, "api/account/me", null, ct);
            var saved = profile.Addresses.SingleOrDefault(a => a.Id == id);
            return saved is null ? NotFound() : View(saved.Address);
        }
        catch (AccountApiException ex) { return Unavailable(ex); }
    }

    [HttpPost]
    public async Task<IActionResult> SaveAddress(ShippingAddress request, string? id, CancellationToken ct)
    {
        ViewData["AddressId"] = id;
        if (ModelState.IsValid)
        {
            try
            {
                await accounts.RequestAsync<SavedAddress>(id is null ? HttpMethod.Post : HttpMethod.Put,
                    id is null ? "api/account/addresses" : $"api/account/addresses/{Uri.EscapeDataString(id)}", request, ct);
                TempData["AccountMessage"] = "Address saved.";
                return RedirectToAction(nameof(Profile));
            }
            catch (AccountApiException ex) { ModelState.AddModelError("", ex.Message); }
        }
        return View("Address", request);
    }

    [HttpPost]
    public async Task<IActionResult> DeleteAddress(string id, CancellationToken ct)
    {
        try { await accounts.RequestAsync<bool>(HttpMethod.Delete, $"api/account/addresses/{Uri.EscapeDataString(id)}", null, ct); }
        catch (AccountApiException ex) { TempData["AccountMessage"] = ex.Message; }
        return RedirectToAction(nameof(Profile));
    }

    [HttpGet]
    public async Task<IActionResult> Orders(CancellationToken ct)
    {
        try { return View(await accounts.RequestAsync<List<OrderReceipt>>(HttpMethod.Get, "api/orders/mine", null, ct)); }
        catch (AccountApiException ex) { return Unavailable(ex); }
    }

    [HttpGet, Authorize(Roles = "Admin")]
    public async Task<IActionResult> Admin(CancellationToken ct)
    {
        try { return View(await accounts.RequestAsync<List<CustomerSummary>>(HttpMethod.Get, "api/account/customers", null, ct)); }
        catch (AccountApiException ex) { return Unavailable(ex); }
    }

    [AllowAnonymous, HttpGet]
    public IActionResult Denied() { Response.StatusCode = 403; return View(); }

    private IActionResult Unavailable(AccountApiException ex)
    {
        Response.StatusCode = ex.StatusCode;
        ViewData["AccountError"] = ex.Message;
        return View("Unavailable");
    }
}
