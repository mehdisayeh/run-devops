using MongoDB.Bson.Serialization.Attributes;
using Velora.Contracts;

namespace Velora.API.Models;

public sealed class StoredOrder
{
    // The unique _id arbitrates concurrent retries across all API replicas.
    [BsonId] public string IdempotencyKey { get; set; } = "";
    public string Fingerprint { get; set; } = "";
    public OrderReceipt Receipt { get; set; } = new();
}
