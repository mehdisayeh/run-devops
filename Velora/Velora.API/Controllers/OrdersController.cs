using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;
using Velora.API.Services;
using Velora.Contracts;
using Microsoft.AspNetCore.Authorization;

namespace Velora.API.Controllers;

[ApiController, Route("api/orders")]
[Authorize(Policy = "CustomerOrGuest")]
[RequestSizeLimit(32768)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class OrdersController(IOrderService orders, ILogger<OrdersController> logger) : ControllerBase
{
    [HttpPost("quote")]
    public Task<ActionResult<CheckoutQuote>> Quote(CartRequest request, CancellationToken ct) =>
        Execute(() => orders.QuoteAsync(request, ct));

    [HttpPost]
    public Task<ActionResult<OrderReceipt>> Place(PlaceOrderRequest request, CancellationToken ct) =>
        Execute(() => orders.PlaceAsync(request, ct));

    [HttpGet("{id}")]
    public async Task<ActionResult<OrderReceipt>> Get(string id, CancellationToken ct)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(id, "\\A[a-f0-9]{64}\\z")) return NotFound();
        return await Execute(async () => await orders.GetAsync(id, ct)
            ?? throw new CheckoutException(404, "Order not found."));
    }

    [HttpGet("mine"), Authorize]
    public Task<ActionResult<IReadOnlyList<OrderReceipt>>> Mine(CancellationToken ct) => Execute(() => orders.MineAsync(ct));

    private async Task<ActionResult<T>> Execute<T>(Func<Task<T>> action)
    {
        try { return Ok(await action()); }
        catch (CheckoutException ex)
        {
            if (ex.StatusCode >= 500) logger.LogError(ex, "Checkout request failed");
            return Problem(statusCode: ex.StatusCode, detail: ex.Message);
        }
        catch (Exception ex) when (ex is MongoException or TimeoutException)
        {
            logger.LogError(ex, "Order storage request failed");
            return Problem(statusCode: 503, detail: "Order storage is temporarily unavailable. Check whether your order was saved before submitting again.");
        }
    }
}
