using System.Net;
using Microsoft.AspNetCore.Mvc;
using Velora.Contracts;
using Velora.Client.Services;

namespace Velora.Client.Controllers;

[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[RequestSizeLimit(32768)]
public sealed class CheckoutController(AccountApiClient accounts, ILogger<CheckoutController> logger) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            try
            {
                var profile = await accounts.RequestAsync<CustomerProfile>(HttpMethod.Get, "api/account/me", null, ct);
                ViewData["SavedAddresses"] = profile.Addresses;
            }
            catch (AccountApiException ex)
            {
                return StatusCode(ex.StatusCode, "Your account is temporarily unavailable. Reload checkout before placing an order.");
            }
        }
        return View();
    }

    [HttpPost, Consumes("application/json"), ValidateAntiForgeryToken]
    public Task<IActionResult> Quote([FromBody] CartRequest request, CancellationToken ct) =>
        Forward("api/orders/quote", request, ct);

    [HttpPost, Consumes("application/json"), ValidateAntiForgeryToken]
    public Task<IActionResult> Place([FromBody] PlaceOrderRequest request, CancellationToken ct) =>
        Forward("api/orders", request, ct);

    [HttpGet]
    public async Task<IActionResult> Confirmation(string id, CancellationToken ct)
    {
        Response.Headers["Referrer-Policy"] = "no-referrer";
        if (id is null || !System.Text.RegularExpressions.Regex.IsMatch(id, "\\A[a-f0-9]{64}\\z")) return NotFound();
        try
        {
            using var response = await accounts.SendAsync(HttpMethod.Get, $"api/orders/{id}", null, ct);
            if (response.StatusCode == HttpStatusCode.NotFound) return NotFound();
            response.EnsureSuccessStatusCode();
            var receipt = await response.Content.ReadFromJsonAsync<OrderReceipt>(cancellationToken: ct);
            return receipt is null ? NotFound() : View(receipt);
        }
        catch (Exception ex) when (ex is AccountApiException or HttpRequestException || ex is TaskCanceledException && !ct.IsCancellationRequested)
        {
            logger.LogWarning("Order confirmation service unavailable");
            return StatusCode(503, "Your confirmation is temporarily unavailable. Reload this page; do not place another order.");
        }
    }

    private async Task<IActionResult> Forward<T>(string path, T request, CancellationToken ct)
    {
        if (Request.Cookies.ContainsKey("Velora.Account") && User.Identity?.IsAuthenticated != true)
            return Unauthorized(new { detail = "Your session expired. Sign in again before placing an order." });
        if (!ModelState.IsValid)
        {
            Response.Headers["X-Checkout-Definitive"] = "true";
            return ValidationProblem(ModelState);
        }
        try
        {
            using var response = await accounts.SendAsync(HttpMethod.Post, path, request, ct);
            if ((int)response.StatusCode is 400 or 409 or 422)
                Response.Headers["X-Checkout-Definitive"] = "true";
            return new ContentResult
            {
                StatusCode = (int)response.StatusCode,
                ContentType = "application/json",
                Content = await response.Content.ReadAsStringAsync(ct)
            };
        }
        catch (Exception ex) when (ex is AccountApiException or HttpRequestException || ex is TaskCanceledException && !ct.IsCancellationRequested)
        {
            logger.LogWarning("Checkout service unavailable");
            return Problem(statusCode: 503, detail: "The order service is temporarily unavailable. Check whether your order was saved before submitting again.");
        }
    }
}
