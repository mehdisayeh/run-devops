using System.Net;
using Microsoft.AspNetCore.Mvc;
using Velora.Contracts;

namespace Velora.Client.Controllers;

[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[RequestSizeLimit(32768)]
public sealed class CheckoutController(IHttpClientFactory clients, ILogger<CheckoutController> logger) : Controller
{
    [HttpGet]
    public IActionResult Index() => View();

    [HttpPost, Consumes("application/json")]
    public Task<IActionResult> Quote([FromBody] CartRequest request, CancellationToken ct) =>
        Forward("api/orders/quote", request, ct);

    [HttpPost, Consumes("application/json")]
    public Task<IActionResult> Place([FromBody] PlaceOrderRequest request, CancellationToken ct) =>
        Forward("api/orders", request, ct);

    [HttpGet]
    public async Task<IActionResult> Confirmation(string id, CancellationToken ct)
    {
        Response.Headers["Referrer-Policy"] = "no-referrer";
        if (id is null || !System.Text.RegularExpressions.Regex.IsMatch(id, "\\A[a-f0-9]{64}\\z")) return NotFound();
        try
        {
            using var response = await clients.CreateClient("Products").GetAsync($"api/orders/{id}", ct);
            if (response.StatusCode == HttpStatusCode.NotFound) return NotFound();
            response.EnsureSuccessStatusCode();
            var receipt = await response.Content.ReadFromJsonAsync<OrderReceipt>(cancellationToken: ct);
            return receipt is null ? NotFound() : View(receipt);
        }
        catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException && !ct.IsCancellationRequested)
        {
            logger.LogWarning("Order confirmation service unavailable");
            return StatusCode(503, "Your confirmation is temporarily unavailable. Reload this page; do not place another order.");
        }
    }

    private async Task<IActionResult> Forward<T>(string path, T request, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            Response.Headers["X-Checkout-Definitive"] = "true";
            return ValidationProblem(ModelState);
        }
        try
        {
            using var response = await clients.CreateClient("Products").PostAsJsonAsync(path, request, ct);
            if ((int)response.StatusCode is 400 or 409 or 422)
                Response.Headers["X-Checkout-Definitive"] = "true";
            return new ContentResult
            {
                StatusCode = (int)response.StatusCode,
                ContentType = "application/json",
                Content = await response.Content.ReadAsStringAsync(ct)
            };
        }
        catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException && !ct.IsCancellationRequested)
        {
            logger.LogWarning("Checkout service unavailable");
            return Problem(statusCode: 503, detail: "The order service is temporarily unavailable. Check whether your order was saved before submitting again.");
        }
    }
}
