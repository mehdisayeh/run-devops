# Velora Catalog API

The two existing endpoints use an injected `IProductRepository`, implemented by `MongoProductRepository`. There is no CQRS or service layer. `ProductContext` was removed. Search, category filtering and sorting remain in the storefront; there is no API pagination. Catalog reads are sorted by numeric ID to retain the original featured order.

`Product.Id` maps to numeric BSON `_id`. `Price` and `Rating` map to BSON Decimal128 while the HTTP contract remains numeric JSON with camelCase property names. All database reads are asynchronous and receive the request cancellation token. A singleton MongoClient manages connection pooling. Startup seeding creates the configured database and collection on the first write if the product collection is missing or empty. It inserts the original 10 products with numeric IDs and Decimal128 values. A nonempty collection is left unchanged; missing products in a partially populated catalog are not backfilled. Concurrent startup uses insert-only upserts to preserve existing product values. MongoDB must be reachable at startup; connection or seed failures stop API startup. Database failures propagate as server failures rather than returning misleading empty results.

## Configuration

`appsettings.json` defaults:

```json
"MongoDB": {
  "ConnectionString": "mongodb://localhost:27017",
  "DatabaseName": "CatalogDb",
  "CollectionName": "Products"
}
```

Override with `MongoDB__ConnectionString`, `MongoDB__DatabaseName`, and `MongoDB__CollectionName`. All three settings are required and validated at startup. An empty or missing product collection is initialized automatically on API startup.

## Local run and verification

From the repository root, with the existing MongoDB container running:

```powershell
dotnet build Velora/Velora.sln
dotnet test Velora/Velora.sln
dotnet run --project Velora/Velora.API
```

In another terminal:

```powershell
Invoke-RestMethod http://localhost:5075/api/Product
Invoke-RestMethod http://localhost:5075/api/Product/1
curl.exe -i http://localhost:5075/api/Product/2147483647
```

The last command returns 404 when the ID is absent. The frontend continues using its existing `CatalogApiUrl` setting.

## Docker

For a standalone API image, build from the solution directory context:

```powershell
docker build -f Velora/Velora.API/Dockerfile -t velora/catalog-api:mongodb Velora
```

Use an existing user-defined network shared with `velora-mongo`, or create and attach one once:

```powershell
docker network create velora-network
docker network connect velora-network velora-mongo
```

Run the API on that network; this uses the existing MongoDB container and its data:

```powershell
docker run --rm --name velora-api --network velora-network -p 5075:8080 -e MongoDB__ConnectionString=mongodb://velora-mongo:27017 -e MongoDB__DatabaseName=CatalogDb -e MongoDB__CollectionName=Products velora/catalog-api:mongodb
```

Verify using the same HTTP commands above. When containerizing the storefront on the same network, set `CatalogApiUrl=http://velora-api:8080/`.

## Tests

`Velora.API.Tests` covers repository-backed controller responses, an empty catalog, unknown IDs, cancellation-token forwarding, numeric Int32/Int64 MongoDB IDs, Decimal128 deserialization and the existing JSON number/property contract. Tests mock the repository and do not require or modify MongoDB.

## Swagger UI

Run `dotnet run --project Velora/Velora.API` and open http://localhost:5075/swagger to explore and try the catalog endpoints. Swagger is enabled in Development. The document is available at `/swagger/v1/swagger.json` and the existing `/openapi/v1.json` URL.

For Docker development, add `-e ASPNETCORE_ENVIRONMENT=Development` to the API container command to enable Swagger.

## Docker Compose (client and API)

Compose reuses your existing seeded `velora-mongo` container through an external network. It does not manage MongoDB or its volumes. Prepare the network once (skip either command if it already exists or is attached):

```powershell
docker network create velora-network
docker network connect velora-network velora-mongo
```

From the repository root:

```powershell
docker compose -f Velora/docker-compose.yml -f Velora/docker-compose.override.yml up --build -d
docker compose -f Velora/docker-compose.yml -f Velora/docker-compose.override.yml ps
Invoke-RestMethod http://localhost:5075/api/Product
```

Open http://localhost:5001 for the storefront or http://localhost:5075/swagger for Swagger. The development override is also loaded automatically when running `docker compose up --build -d` from the `Velora` directory. Visual Studio's Docker Compose profile starts both projects and opens Swagger.

Overrides: `VELORA_NETWORK`, `VELORA_API_PORT`, `VELORA_CLIENT_PORT`, and the three `MongoDB__...` settings. Set these in your shell or a Compose `.env` file. The base file configures internal networking; the override enables Development and publishes host ports.

Stop only the Compose-managed application services:

```powershell
docker compose -f Velora/docker-compose.yml -f Velora/docker-compose.override.yml down
```

The existing MongoDB container, volumes and external network remain in place.

See [guest checkout and orders](../CHECKOUT.md) for order endpoints, order endpoints and integration tests.

See [customer accounts](../ACCOUNTS.md) for registration, sign-in, profiles, saved addresses, customer/admin rules, deployment and verification.
