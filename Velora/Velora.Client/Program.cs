using Microsoft.AspNetCore.DataProtection;

var builder = WebApplication.CreateBuilder(args);
var protection = builder.Services.AddDataProtection().SetApplicationName("Velora.Client");
if (builder.Configuration["DataProtection:KeysPath"] is { Length: > 0 } keysPath)
    protection.PersistKeysToFileSystem(new DirectoryInfo(keysPath));

// Add services to the container.
builder.Services.AddControllersWithViews();
builder.Services.AddAntiforgery(options => options.HeaderName = "X-CSRF-TOKEN");
builder.Services.AddHttpClient("Products", client =>
{
    var baseUrl = builder.Configuration["CatalogApiUrl"]
        ?? throw new InvalidOperationException("CatalogApiUrl must be configured.");
    client.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
    client.Timeout = TimeSpan.FromSeconds(60);
});
builder.Services.AddScoped<Velora.Client.Services.ProductApiClient>(services =>
    new(services.GetRequiredService<IHttpClientFactory>().CreateClient("Products")));

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
}
app.UseRouting();

app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();


app.Run();
