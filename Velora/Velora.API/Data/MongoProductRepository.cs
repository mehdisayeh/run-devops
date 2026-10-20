using MongoDB.Driver;
using Velora.API.Models;

namespace Velora.API.Data;

public sealed class MongoProductRepository(IMongoCollection<Product> products) : IProductRepository
{
    public async Task<IReadOnlyList<Product>> GetAllAsync(CancellationToken cancellationToken) =>
        await products.Find(Builders<Product>.Filter.Empty)
            .SortBy(product => product.Id)
            .ToListAsync(cancellationToken);

    public async Task<Product?> GetByIdAsync(int id, CancellationToken cancellationToken) =>
        await products.Find(product => product.Id == id).FirstOrDefaultAsync(cancellationToken);
}
