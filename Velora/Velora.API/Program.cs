using Microsoft.Extensions.Options;
using MongoDB.Driver;
using Velora.API.Data;
using Velora.API.Models;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
builder.Services.AddOptions<MongoDbSettings>()
    .BindConfiguration(MongoDbSettings.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddSingleton<IMongoClient>(services =>
    new MongoClient(services.GetRequiredService<IOptions<MongoDbSettings>>().Value.ConnectionString));
builder.Services.AddSingleton<IMongoCollection<Product>>(services =>
{
    var settings = services.GetRequiredService<IOptions<MongoDbSettings>>().Value;
    return services.GetRequiredService<IMongoClient>()
        .GetDatabase(settings.DatabaseName)
        .GetCollection<Product>(settings.CollectionName);
});
builder.Services.AddScoped<IProductRepository, MongoProductRepository>();
builder.Services.AddSwaggerGen(options =>
    options.SwaggerDoc("v1", new() { Title = "Velora Catalog API", Version = "v1" }));

var app = builder.Build();

// Configure the HTTP request pipeline.
app.UseSwagger();
app.UseSwagger(options => options.RouteTemplate = "openapi/{documentName}.json");
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("v1/swagger.json", "Velora Catalog API v1");
    options.DocumentTitle = "Velora Catalog API";
});
app.UseAuthorization();

app.MapControllers();

app.Run();
