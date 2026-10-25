using Velora.Contracts;

namespace Velora.API.Services;

public interface IOrderService
{
    Task<CheckoutQuote> QuoteAsync(CartRequest request, CancellationToken cancellationToken);
    Task<OrderReceipt> PlaceAsync(PlaceOrderRequest request, CancellationToken cancellationToken);
    Task<OrderReceipt?> GetAsync(string id, CancellationToken cancellationToken);
    Task<IReadOnlyList<OrderReceipt>> MineAsync(CancellationToken cancellationToken);
}
