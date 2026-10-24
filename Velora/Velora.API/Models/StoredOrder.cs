using MongoDB.Bson.Serialization.Attributes;
using Velora.Contracts;

namespace Velora.API.Models;

[BsonIgnoreExtraElements]
public sealed class StoredOrder
{
    // Internal MongoDB order document identifier.
    [BsonId] public string IdempotencyKey { get; set; } = "";
    public OrderReceipt Receipt { get; set; } = new();
}
