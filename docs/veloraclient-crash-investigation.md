# Velora.Client AKS crash investigation — 2026-10-25

## Confirmed deployment defect

The Client deployment runs the API binary. The Client pipeline used
`dockerfilePath: '**/Dockerfile'` with a build context containing both projects.
The deployed image proves that the API Dockerfile was selected.

Read-only inspection of the current `veloraAKSCluster` context found:

- Deployment image: `veloradevacr.azurecr.io/veloraclient:14` (pipeline build 14).
- All three pods resolve to
  `sha256:f2b12c38f2854db55de8cdba53393e9995e2d4e418006e90cf390c99abe0a1ed`.
- Image entrypoint: `["dotnet","Velora.API.dll"]`; `/app` contains API and
  MongoDB assemblies, with no Client assembly.
- Image architecture: amd64; the single AKS node is also amd64.
- Installed image runtimes: Microsoft.NETCore.App and Microsoft.AspNetCore.App
  9.0.21. Both source projects and Dockerfiles target .NET 9.
- Pods exit with `Error/139` after approximately 32 seconds, without an
  `OOMKilled` termination reason. No liveness probe is configured.
- Logs became available during investigation. They show `Hosting failed to
  start`, followed by an unhandled MongoDB `System.TimeoutException` selecting
  `localhost:27017` after 29999 ms. The stack identifies
  `Velora.API.Data.CatalogSeeder.StartAsync`, line 13, and API `Program.cs`, line 49.
- API `Program.cs` registers the seeder at line 30. API `appsettings.json`, line
  10, defaults to `mongodb://localhost:27017`. The Client deployment correctly
  supplies only `CatalogApiUrl`, not a MongoDB connection string.

The exact registry digest was pulled and reproduced locally with the Client's
environment, 128 MiB memory and 0.5 CPU. It exited with **139**,
`OOMKilled=false`, after 31 seconds and the same API startup exception.
This confirms the wrong-image failure independently of AKS. A native stack dump
was not collected, so the precise runtime mechanism converting the fatal
exception into exit 139 is not established. There is no evidence of a Client
HttpClient timeout causing a segmentation fault, architecture mismatch, or layer
corruption. Do not add MongoDB credentials to the Client to accommodate this image.

## Repository fixes

- Both pipelines select their respective Dockerfiles by explicit absolute path.
- Client trigger includes the correctly spelled AKS manifest and shared contracts.
- Client container port declarations match the runtime's 8080. The local k8s
  Service target is also corrected to 8080; the AKS Service already targeted 8080.
  These port fixes are separate from the crash cause.
- No MVC code, runtime version, API URL, memory limit, or MongoDB configuration
  change is required for this incident. Compose already selects explicit files
  and uses Client → API port 8080. AKS uses `http://veloraapi-service`, whose
  Service maps port 80 to API port 8080.

## Validation

- `dotnet restore Velora/Velora.sln`: passed.
- `dotnet build Velora/Velora.sln -c Release --no-restore`: passed, no warnings/errors.
- `dotnet test Velora/Velora.API.Tests/Velora.API.Tests.csproj -c Release --no-build --no-restore`:
  15 passed, 4 MongoDB integration tests skipped (isolated test URI not configured).
- `docker build --pull --platform linux/amd64 -f Velora/Velora.Client/Dockerfile -t veloraclient:crash-fix-local Velora`:
  passed (build layers were cached). Entrypoint verified as `dotnet Velora.Client.dll`.
- Correct Client starts and serves `/css/site.css` with HTTP 200 under the AKS
  memory/CPU limits. An unavailable API produces HTTP 500 on MVC pages while
  the process stays running, rather than reproducing the fatal API startup.
- In a separate local Docker network, MongoDB plus the API binary and the correct
  Client image served `/` and `/Checkout` with HTTP 200. The API seeded ten
  products successfully. Client resource limits remained 128 MiB and 0.5 CPU.

## Rebuild and deploy, only after approval

Run from the repository root in PowerShell. Use a new unique tag; never overwrite
the failing tag or rely on `latest` with `IfNotPresent`.

```powershell
$tag = 'client-fix-' + (Get-Date -Format 'yyyyMMddHHmmss')
$image = "veloradevacr.azurecr.io/veloraclient:$tag"
az acr login --name veloradevacr
docker build --pull --platform linux/amd64 -f Velora/Velora.Client/Dockerfile -t $image Velora
docker image inspect $image --format '{{.Architecture}} {{json .Config.Entrypoint}}'
# Verify amd64 and ["dotnet","Velora.Client.dll"] before pushing.
docker push $image
# Use the digest printed by push to make deployment immutable.
$digest = '<sha256:digest-from-push>'
kubectl --context veloraAKSCluster -n default set image deployment/veloraclient-deployment "veloraclient=veloradevacr.azurecr.io/veloraclient@$digest"
kubectl --context veloraAKSCluster -n default rollout status deployment/veloraclient-deployment --timeout=180s
kubectl --context veloraAKSCluster -n default get pods -l app=veloraclient -o wide
kubectl --context veloraAKSCluster -n default logs deployment/veloraclient-deployment --tail=100
kubectl --context veloraAKSCluster -n default port-forward service/veloraclient-service 18080:80
# In a second terminal:
curl.exe -i http://127.0.0.1:18080/
```

Alternatively, review and merge the pipeline fix, then run the Client pipeline
through its normal deployment approval process. It builds and deploys the unique
Build.BuildId tag. No registry push or AKS mutation was performed in this investigation.

## Repeatable evidence commands

```powershell
kubectl --context veloraAKSCluster -n default describe deployment veloraclient-deployment
kubectl --context veloraAKSCluster -n default describe pods -l app=veloraclient
kubectl --context veloraAKSCluster -n default get events --sort-by=.metadata.creationTimestamp
kubectl --context veloraAKSCluster -n default get deployment veloraclient-deployment -o yaml
kubectl --context veloraAKSCluster -n default get pods -l app=veloraclient -o wide
kubectl --context veloraAKSCluster -n default get pods -l app=veloraclient -o json
kubectl --context veloraAKSCluster -n default logs deployment/veloraclient-deployment --tail=100
kubectl --context veloraAKSCluster -n default logs <pod-name> --previous --tail=100
docker image inspect veloradevacr.azurecr.io/veloraclient:14 --format '{{.Architecture}} {{json .Config.Entrypoint}}'
```

Avoid exporting Kubernetes Secrets or container environments containing MongoDB credentials.
