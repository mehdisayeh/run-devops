using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Driver;
using Moq;
using Velora.API.Data;
using Velora.API.Models;

namespace Velora.API.Tests;

public class CatalogSeederTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task SeedsOnlyAnEmptyCatalog(long count)
    {
        using var source = new CancellationTokenSource();
        var collection = new Mock<IMongoCollection<Product>>();
        collection.Setup(c => c.CountDocumentsAsync(It.IsAny<FilterDefinition<Product>>(),
            It.IsAny<CountOptions>(), source.Token)).ReturnsAsync(count);
        var seeder = new CatalogSeeder(collection.Object, NullLogger<CatalogSeeder>.Instance);

        await seeder.StartAsync(source.Token);

        collection.Verify(c => c.BulkWriteAsync(
            It.Is<IEnumerable<WriteModel<Product>>>(writes => writes.Count() == 10 &&
                writes.All(write => write is UpdateOneModel<Product> && ((UpdateOneModel<Product>)write).IsUpsert)),
            It.Is<BulkWriteOptions>(options => !options.IsOrdered), source.Token),
            count == 0 ? Times.Once() : Times.Never());
    }
}
