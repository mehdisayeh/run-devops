using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Velora.Client.Services;

var builder = WebApplication.CreateBuilder(args);
var protection = builder.Services.AddDataProtection().SetApplicationName("Velora.Client");
if (builder.Configuration["DataProtection:KeysPath"] is { Length: > 0 } keysPath)
    protection.PersistKeysToFileSystem(new DirectoryInfo(keysPath));
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<AccountApiClient>();
builder.Services.AddScoped<CustomerCookieEvents>();
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(options =>
{
    options.Cookie.Name = "Velora.Account";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
    options.LoginPath = "/Account/Login";
    options.AccessDeniedPath = "/Account/Denied";
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.SlidingExpiration = false;
    options.EventsType = typeof(CustomerCookieEvents);
});
builder.Services.AddAuthorization();
builder.Services.AddAntiforgery(options => options.HeaderName = "X-CSRF-TOKEN");

// Add services to the container.
builder.Services.AddControllersWithViews();
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
app.UseAuthentication();
app.Use(async (context, next) =>
{
    if (context.Items.ContainsKey("AccountUnavailable"))
    {
        context.Response.StatusCode = 503;
        await context.Response.WriteAsync("Accounts are temporarily unavailable. Please try again later.");
        return;
    }
    await next();
});
app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();


app.Run();
