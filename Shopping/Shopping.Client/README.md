# FORME storefront

ASP.NET Core MVC (.NET 9) with the existing Bootstrap 5.3.3 assets and vanilla JavaScript. No database, API, authentication, payment processing, or added runtime package dependencies.

## Run

```powershell
dotnet build Shopping/Shopping.sln
dotnet run --project Shopping/Shopping.Client --no-launch-profile --urls http://localhost:5088
```

Open http://localhost:5088. Run commands from the repository root.

## Structure

- `Data/ProductContext.cs`: the original hardcoded `List<Product>`; the single source for all product metadata.
- `HomeController`: catalog, product details (404 for unknown IDs), cart and privacy pages.
- `Views/Shared/_Layout.cshtml`: shared navigation, footer and safely serialized catalog data.
- `Views/Shared/_ProductCard.cshtml`: reusable product presentation.
- `wwwroot/css/site.css`: responsive design tokens and styles.
- `wwwroot/js/site.js`: search, category filters, sorting and saved bag.
- `wwwroot/images`: local product photography; see IMAGE_CREDITS.md.

Search supports product names, brands, category, descriptions and SKU. Category and search are reflected in URL parameters and compose with sorting. The navigation search works from every page.

The bag uses `forme.cart.v1` in localStorage and contains only product IDs and quantities. Prices and stock are read from the current catalog, invalid entries are discarded, duplicate entries merged, quantities capped to stock, and multiple tabs synchronized. Storage failures produce a visible message. This is a client-side bag, with no checkout or order submission.

For a future database, replace controller reads from ProductContext with an injected catalog service and maintain the Razor model contract. Prices and inventory must be validated on the server when order processing is introduced.

Accessibility includes labeled controls, semantic landmarks, keyboard focus styles, live search/cart announcements, a skip link and reduced-motion support. The layout adapts at 1100px and 767px; product grids use two columns on mobile. Typography uses Google Fonts with local system fallbacks.
