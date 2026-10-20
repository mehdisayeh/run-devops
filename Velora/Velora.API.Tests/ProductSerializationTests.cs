using System.Text.Json;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using Velora.API.Models;

namespace Velora.API.Tests;

public class ProductSerializationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NumericMongoIdAndDecimal128PreserveTheJsonContract(bool useInt64)
    {
        var document = new BsonDocument
        {
            { "_id", useInt64 ? (BsonValue)new BsonInt64(42) : new BsonInt32(42) },
            { "Name", "Database product" },
            { "Price", new BsonDecimal128(799.99m) },
            { "Rating", new BsonDecimal128(4.8m) }
        };

        var product = BsonSerializer.Deserialize<Product>(document);
        Assert.Equal(42, product.Id);
        Assert.Equal(799.99m, product.Price);
        Assert.Equal(4.8m, product.Rating);
        var bson = product.ToBsonDocument();
        Assert.Equal(42, bson["_id"].AsInt32);
        Assert.Equal(BsonType.Decimal128, bson["Price"].BsonType);
        Assert.Equal(BsonType.Decimal128, bson["Rating"].BsonType);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(product, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        Assert.Equal(42, json.RootElement.GetProperty("id").GetInt32());
        Assert.Equal(799.99m, json.RootElement.GetProperty("price").GetDecimal());
        Assert.False(json.RootElement.TryGetProperty("_id", out _));
    }
}
