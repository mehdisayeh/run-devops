# VELORA storefront

ASP.NET Core MVC (.NET 9) with the existing Bootstrap 5.3.3 assets and vanilla JavaScript. The product catalog is served by the Velora.API project. No database, authentication or payment processing.

## Run

```powershell
dotnet build Velora/Velora.sln
# Start the API in a separate terminal first:
dotnet run --project Velora/Velora.API
dotnet run --project Velora/Velora.Client --no-launch-profile --urls http://localhost:5088
```

Open http://localhost:5088. Run commands from the repository root.

## Docker

Run these commands from the repository root:

```powershell
docker build -f Velora/Velora.Client/Dockerfile -t velora/client:1.0.0 Velora
docker run --name velora-client --rm -p 5088:8080 -e Api__BaseUrl=http://host.docker.internal:5075/ velora/client:1.0.0
```

Open http://localhost:5088. The Docker build context is the `Velora` solution folder. The API must be running and reachable from the container. For a host-run API, bind it to http://0.0.0.0:5075 with --urls; in a container network, set Api__BaseUrl to the API service URL.

## Structure

- `../Velora.API/Data/MongoProductRepository.cs`: reads the existing MongoDB catalog.
- `Services/ProductApiClient.cs`: scoped HTTP client used by controllers and the shared layout.
- `HomeController`: catalog, product details (404 for unknown IDs), cart and privacy pages.
- `Views/Shared/_Layout.cshtml`: shared navigation, footer and safely serialized catalog data.
- `Views/Shared/_ProductCard.cshtml`: reusable product presentation.
- `wwwroot/css/site.css`: responsive design tokens and styles.
- `wwwroot/js/site.js`: search, category filters, sorting and saved bag.
- `wwwroot/images`: local product photography; see IMAGE_CREDITS.md.

Search supports product names, brands, category, descriptions and SKU. Category and search are reflected in URL parameters and compose with sorting. The navigation search works from every page.

The bag uses `forme.cart.v1` in localStorage and contains only product IDs and quantities. Prices and stock are read from the current catalog, invalid entries are discarded, duplicate entries merged, quantities capped to stock, and multiple tabs synchronized. Storage failures produce a visible message. This is a client-side bag, with no checkout or order submission.

The API exposes GET /api/Product and GET /api/Product/{id} (404 for unknown IDs). Configure the storefront with Api:BaseUrl in appsettings.json or the Api__BaseUrl environment variable; the default is http://localhost:5075/. API requests run on the MVC server, so browser CORS configuration is unnecessary. MongoDB configuration and Docker instructions are documented in `../Velora.API/README.md`. Prices and inventory must be validated on the server when order processing is introduced.

Accessibility includes labeled controls, semantic landmarks, keyboard focus styles, live search/cart announcements, a skip link and reduced-motion support. The layout adapts at 1100px and 767px; product grids use two columns on mobile. Typography uses Google Fonts with local system fallbacks.
