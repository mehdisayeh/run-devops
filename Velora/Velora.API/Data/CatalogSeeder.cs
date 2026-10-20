using MongoDB.Driver;
using Velora.API.Models;

namespace Velora.API.Data;

public sealed class CatalogSeeder(
    IMongoCollection<Product> products,
    ILogger<CatalogSeeder> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // MongoDB creates the database and collection on the first write.
        if (await products.CountDocumentsAsync(Builders<Product>.Filter.Empty,
                new CountOptions { Limit = 1 }, cancellationToken) > 0)
        {
            logger.LogInformation("Catalog already contains products; skipping seed");
            return;
        }

        // SetOnInsert preserves existing values if two API instances start together.
        var writes = ProductSeedData.Products.Select(product =>
            (WriteModel<Product>)new UpdateOneModel<Product>(
                Builders<Product>.Filter.Eq(p => p.Id, product.Id),
                Builders<Product>.Update
                    .SetOnInsert(p => p.Sku, product.Sku)
                    .SetOnInsert(p => p.Name, product.Name)
                    .SetOnInsert(p => p.Brand, product.Brand)
                    .SetOnInsert(p => p.Category, product.Category)
                    .SetOnInsert(p => p.Description, product.Description)
                    .SetOnInsert(p => p.Price, product.Price)
                    .SetOnInsert(p => p.Currency, product.Currency)
                    .SetOnInsert(p => p.Rating, product.Rating)
                    .SetOnInsert(p => p.ReviewCount, product.ReviewCount)
                    .SetOnInsert(p => p.StockQuantity, product.StockQuantity)
                    .SetOnInsert(p => p.Badge, product.Badge)
                    .SetOnInsert(p => p.Icon, product.Icon)
                    .SetOnInsert(p => p.ImageFile, product.ImageFile)) { IsUpsert = true });
        await products.BulkWriteAsync(writes, new BulkWriteOptions { IsOrdered = false }, cancellationToken);
        logger.LogInformation("Catalog initialized with {Count} seed products", ProductSeedData.Products.Count);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
