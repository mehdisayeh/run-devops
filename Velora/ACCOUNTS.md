# Customer accounts

Customers can register at `/Account/Register`, sign in at `/Account/Login`, and open
`/Account/Profile` from the navigation. The account page provides a name editor,
saved-address add/edit/remove forms (up to 10 addresses), sign-out, and `/Account/Orders`.
The sign-in email is read-only after registration. Checkout offers saved addresses to
signed-in customers; saving a new address remains an explicit action in the account page.

New signed-in orders record the authenticated customer ID internally. Only that customer
or an admin can retrieve those orders, even with the receipt URL. `/api/orders/mine`
always uses the current identity. Guest checkout remains available and old guest receipts
keep their private-link access. Matching an email never claims an old guest order.

## Authentication and access rules

The API uses random 256-bit bearer sessions stored in `CustomerSessions`; only SHA-256
token hashes are persisted. Sessions expire after eight hours, are checked for expiry on
every request, and are revoked on sign-out. A TTL index removes expired records later.
The storefront puts its API token inside an encrypted HttpOnly `Velora.Account` cookie;
it never puts the token in HTML, JavaScript, localStorage or sessionStorage. Cookies use
SameSite=Lax, no sliding renewal, and Secure outside Development. The API re-reads the
customer and roles for every authenticated request, including MVC cookie validation.
Deleted accounts and changed roles therefore take effect on the next request.

Passwords use ASP.NET Core's salted Identity PasswordHasher with 210,000 PBKDF2
iterations. Registration requires 12–128 characters. Five failed password attempts lock
that account for 15 minutes; failed attempts use a generic message. A per-IP, per-API-pod
limit also bounds registration/login to 60 requests per minute. When the MVC app proxies
requests, the API sees the MVC pod IP, so this limit is shared by users of that pod.
For a public deployment, configure ingress rate limits for the real client IP as well.

The `Customers.NormalizedEmail` unique index prevents duplicate accounts, including
case variations and concurrent registration. Profile and address routes use the current
identity; there is no client-supplied customer-ID field or editable role field. Registration
always assigns `Customer`. Password hashes and session records are never profile DTOs.

All MVC account writes and checkout POSTs validate antiforgery tokens. The API accepts
bearer tokens, not browser cookies. Failed bearer authentication cannot fall back to a
guest order. Login return URLs must be local. Private responses use no-store. Account
storage outages fail closed, with a retry message instead of authorizing stale roles.

Email verification, password recovery/change, MFA, and social sign-in are not part of
this milestone. No emails are sent. Successful registration takes the customer to sign-in.

## Admin access

`/Account/Admin` and `GET /api/account/customers` require `Admin` on the server. They
show the 100 newest customers' names, emails, roles and registration dates. These routes
do not expose password hashes, tokens or saved addresses. No admin account/password is
seeded, and there is no public role-grant endpoint.

Register the intended administrator normally. An operator with database access can
then grant the role in mongosh, substituting the actual database/email:

```javascript
db.getSiblingDB("CatalogDb").Customers.updateOne(
  { NormalizedEmail: "YOUR.EMAIL@EXAMPLE.COM" },
  { $set: { Roles: ["Customer", "Admin"] } }
)
```

Set `Roles` back to `["Customer"]` to revoke admin access. Check `matchedCount` to
confirm the intended registered customer was selected. Existing sessions see the change
on the next request. No roles have been granted to real accounts by this implementation.

## API endpoints

| Endpoint | Access / body |
| --- | --- |
| `POST /api/account/register` | Anonymous: `fullName`, `email`, `password` |
| `POST /api/account/login` | Anonymous: `email`, `password`; returns session token/expiry/profile |
| `POST /api/account/logout` | Signed in; revokes this bearer session |
| `GET /api/account/me` | Signed in; returns own profile and saved addresses |
| `PUT /api/account/me` | Signed in: `fullName` only |
| `POST /api/account/addresses` | Signed in: ShippingAddress fields from checkout |
| `PUT /api/account/addresses/{id}` | Signed in: edits an address belonging to the current customer |
| `DELETE /api/account/addresses/{id}` | Signed in: removes own saved address |
| `GET /api/orders/mine` | Signed in: 100 latest own orders |
| `GET /api/account/customers` | Admin only |

## Local and AKS deployment

Use the existing MongoDB settings and `CatalogApiUrl`. Startup creates customer-email,
session-expiry, and order-customer indexes; the API's MongoDB identity needs index/create
and read/write permissions for `Customers`, `CustomerSessions`, and `Orders`. These
collection names are reserved and cannot be used as the product collection.

Rebuild both API and frontend images; deploy the API before the frontend. The Compose
client now mounts a named key-ring volume at `/var/velora/keys`. The Dockerfile prepares
that directory for the non-root app user. This retains login/antiforgery keys across
container recreation. The AKS client manifest includes an Azure Files ReadWriteMany PVC
using `azurefile-csi` and mounts it on every client replica. The existing client pipeline
already applies that complete manifest. No cluster deployment has been performed.

`DataProtection__KeysPath` sets the shared key directory. Every frontend replica in the
same environment must use this directory and the fixed application name `Velora.Client`.
Restrict access to this key ring; it protects authentication and antiforgery cookies.
Retain it through rollouts and backups. Use separate key storage per environment.

For real customer accounts, serve both browser-to-frontend and frontend-to-API traffic
over HTTPS and set `ASPNETCORE_ENVIRONMENT=Production`. Production cookies require HTTPS.
The current Development manifests retain their existing local/development behavior;
configure TLS and Production before using them for real customer data.

## Verification

```powershell
$env:VELORA_ACCOUNT_TEST_MONGO='mongodb://localhost:27017/?replicaSet=rs0&directConnection=true'
$env:VELORA_CHECKOUT_TEST_MONGO=$env:VELORA_ACCOUNT_TEST_MONGO
dotnet test Velora/Velora.sln
node --test Velora/Velora.Client.Tests/checkout.test.cjs
```

Account integration tests create/drop only fresh `velora_accounts_test_*` databases.
They exercise normalized unique registration, password storage, lockout, hashed/revoked/
expired sessions, address limits and ownership, role changes, and order isolation.
Without `VELORA_ACCOUNT_TEST_MONGO`, those tests explicitly skip.

The HTTP smoke test verifies the real API/MVC middleware, forms, cookies, CSRF checks,
customer/admin authorization, role revocation, local return URLs and order access.
Start a temporary API on `http://localhost:5079` with `MongoDB__DatabaseName` set to a
fresh name such as `velora_accounts_smoke_local`, and a Development frontend on
`http://localhost:5090` pointing `CatalogApiUrl` at that API. Then run:

```powershell
$env:VELORA_ACCOUNT_SMOKE_DATABASE='velora_accounts_smoke_local'
python Velora/tests/accounts_http_smoke.py
```

The smoke test uses mongosh in the local `velora-db` container to change synthetic
accounts' roles/expiry in that disposable database. Use a new database for each run and
remove only that disposable database after stopping the temporary apps. It does not
change the normal `CatalogDb` database.

Implementation references: [ASP.NET Core cookie authentication](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/cookie?view=aspnetcore-9.0),
[Data Protection configuration](https://learn.microsoft.com/en-us/aspnet/core/security/data-protection/configuration/overview?view=aspnetcore-9.0),
and [Azure Files volumes for AKS](https://learn.microsoft.com/azure/aks/azure-files-volume).
