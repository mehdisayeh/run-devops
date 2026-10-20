using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Velora.API.Models;

public class Product
{
    [BsonId]
    public int Id { get; set; }
    public string Sku { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Brand { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    [BsonRepresentation(BsonType.Decimal128)]
    public decimal Price { get; set; }
    public string Currency { get; set; } = "EUR";
    [BsonRepresentation(BsonType.Decimal128)]
    public decimal Rating { get; set; }
    public int ReviewCount { get; set; }
    public int StockQuantity { get; set; }
    public string Badge { get; set; } = string.Empty;
    public string Icon { get; set; } = "📦";
    public string ImageFile { get; set; } = string.Empty;
}
