using System.Net;
using Velora.Client.Models;

namespace Velora.Client.Services;

public sealed class ProductApiClient(HttpClient httpClient)
{
    private Task<List<Product>>? _products;

    // Reuse the catalog response between the controller and layout within a request.
    public Task<List<Product>> GetProductsAsync(CancellationToken cancellationToken = default) =>
        _products ??= LoadProductsAsync(cancellationToken);

    private async Task<List<Product>> LoadProductsAsync(CancellationToken cancellationToken) =>
        await httpClient.GetFromJsonAsync<List<Product>>("api/Product", cancellationToken)
        ?? throw new InvalidOperationException("The product API returned an empty response.");

    public async Task<Product?> GetProductAsync(int id, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.GetAsync($"api/Product/{id}", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<Product>(cancellationToken)
            ?? throw new InvalidOperationException("The product API returned an empty response.");
    }
}
