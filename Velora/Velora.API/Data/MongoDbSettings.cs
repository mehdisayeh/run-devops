using System.ComponentModel.DataAnnotations;

namespace Velora.API.Data;

public sealed class MongoDbSettings
{
    public const string SectionName = "MongoDB";

    [Required]
    public string ConnectionString { get; set; } = string.Empty;
    [Required]
    public string DatabaseName { get; set; } = string.Empty;
    [Required]
    public string CollectionName { get; set; } = string.Empty;
}
