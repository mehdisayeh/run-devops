using Microsoft.Extensions.Options;
using MongoDB.Driver;
using Velora.API.Models;

namespace Velora.API.Data;

public sealed class OrderIndexes(IMongoClient client, IOptions<MongoDbSettings> settings) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var orders = client.GetDatabase(settings.Value.DatabaseName).GetCollection<StoredOrder>("Orders");
        await orders.Indexes.CreateOneAsync(new CreateIndexModel<StoredOrder>(
            Builders<StoredOrder>.IndexKeys.Ascending(o => o.Receipt.Id),
            new CreateIndexOptions { Unique = true, Name = "receipt_id" }), cancellationToken: cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
