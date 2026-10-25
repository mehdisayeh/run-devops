using Microsoft.Extensions.Options;
using MongoDB.Driver;
using Velora.API.Models;

namespace Velora.API.Data;

public sealed class AccountIndexes(IMongoClient client, IOptions<MongoDbSettings> settings) : IHostedService
{
    public async Task StartAsync(CancellationToken ct)
    {
        var db = client.GetDatabase(settings.Value.DatabaseName);
        await db.GetCollection<Customer>("Customers").Indexes.CreateOneAsync(new CreateIndexModel<Customer>(
            Builders<Customer>.IndexKeys.Ascending(c => c.NormalizedEmail), new CreateIndexOptions { Unique = true }), cancellationToken: ct);
        await db.GetCollection<CustomerSession>("CustomerSessions").Indexes.CreateOneAsync(new CreateIndexModel<CustomerSession>(
            Builders<CustomerSession>.IndexKeys.Ascending(s => s.ExpiresAtUtc), new CreateIndexOptions { ExpireAfter = TimeSpan.Zero }), cancellationToken: ct);
        await db.GetCollection<Models.StoredOrder>("Orders").Indexes.CreateOneAsync(new CreateIndexModel<StoredOrder>(
            Builders<Models.StoredOrder>.IndexKeys.Ascending(o => o.CustomerId)), cancellationToken: ct);
    }
    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
