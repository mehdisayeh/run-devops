using Microsoft.Extensions.Options;
using MongoDB.Driver;
using Velora.API.Data;
using Velora.API.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);
MongoDB.Bson.Serialization.BsonSerializer.RegisterSerializer(
    new MongoDB.Bson.Serialization.Serializers.DecimalSerializer(MongoDB.Bson.BsonType.Decimal128));

// Add services to the container.

builder.Services.AddControllers();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<Velora.API.Services.AccountService>();
builder.Services.Configure<PasswordHasherOptions>(options => options.IterationCount = 210000);
builder.Services.AddSingleton<IPasswordHasher<Customer>, PasswordHasher<Customer>>();
builder.Services.AddAuthentication("CustomerBearer")
    .AddScheme<AuthenticationSchemeOptions, Velora.API.Authentication.CustomerAuthentication>("CustomerBearer", _ => { });
builder.Services.AddAuthorization(options => options.AddPolicy("CustomerOrGuest", policy =>
    policy.RequireAssertion(context => context.User.Identity?.IsAuthenticated == true ||
        context.Resource is HttpContext http && !http.Request.Headers.ContainsKey("Authorization"))));
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = 429;
    options.AddPolicy("account-login", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 60, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});
builder.Services.AddOptions<MongoDbSettings>()
    .BindConfiguration(MongoDbSettings.SectionName)
    .ValidateDataAnnotations()
    .Validate(settings => !new[] { "Orders", "Customers", "CustomerSessions" }.Contains(settings.CollectionName, StringComparer.OrdinalIgnoreCase),
        "The product collection cannot use a reserved application collection name.")
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
builder.Services.AddScoped<Velora.API.Services.IOrderService, Velora.API.Services.MongoOrderService>();
builder.Services.AddHostedService<CatalogSeeder>();
builder.Services.AddHostedService<OrderIndexes>();
builder.Services.AddHostedService<AccountIndexes>();
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
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
