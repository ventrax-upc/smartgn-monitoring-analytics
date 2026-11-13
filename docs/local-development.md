# Local development

## Requirements

- Docker Desktop with the Linux engine, Docker Compose v2 and PowerShell 7 for the standard local flow.
- .NET 10 SDK only when running the application directly on the host.

## Run and inspect

From the repository root:

- Prepare and start the standalone composition: ./scripts/start-local.ps1.
- Check health, readiness and Swagger: ./scripts/smoke-local.ps1.
- Stop containers without deleting their volumes: docker compose --env-file .env -f compose.yml down.
- Start the service directly: dotnet run --project src/Smartgn.Monitoring/Smartgn.Monitoring.csproj.
- Open Swagger UI: http://127.0.0.1:8084/swagger-ui.html.
- Read the OpenAPI document: http://127.0.0.1:8084/v3/api-docs.
- Build the Docker image: docker build -t smartgn-monitoring-analytics .
- Run the test suite: ./scripts/test.ps1 or dotnet test Smartgn.Monitoring.slnx.

The Compose stack starts only this service, its PostgreSQL database and Redis. It does not start or connect IAM, Telemetry, Billing or the Gateway. Protected analytics requests that call those dependencies require their later integrated configuration.

The container listens on port 8084. The local preparation script creates ignored .env values without replacing existing settings. PostgreSQL data is stored in the monitoring-postgres-data Docker volume.

In the integrated Compose, `SMARTGN_CONFIG_URL` optionally loads `smartgn-monitoring/local` from Config Server during startup, and `SMARTGN_ADMIN_URL` enables authenticated registration of `/health` in Admin. `SMARTGN_CONFIG_PROFILE` selects another profile. Local environment variables override remotely loaded values. These settings are omitted in standalone runs.

Monitor cache counters with dotnet-counters monitor -n Smartgn.Monitoring --counters SmartGN.Monitoring.Cache.

## Configuration

ASP.NET Core environment variables use double underscores for nested configuration keys.

| Configuration key | Environment variable | Default |
| --- | --- | --- |
| ConnectionStrings:DefaultConnection | ConnectionStrings__DefaultConnection | PostgreSQL database smartgn_monitoring on localhost |
| ConnectionStrings:RedisConnection | ConnectionStrings__RedisConnection | localhost:6379 |
| Jwt:Secret | JWT_SECRET | Required; use the IAM key, at least 32 bytes |
| Integration:InternalServiceToken | INTERNAL_SERVICE_TOKEN | Empty for local development |
| Integration:IamBaseUrl | IAM_BASE_URL | http://localhost:8080 |
| GrpcServers:Telemetry | TELEMETRY_GRPC_URL | http://localhost:9084 |
| GrpcServers:Billing | BILLING_GRPC_URL | http://localhost:9083 |
| TelemetryMetrics:* | — | totalVolumeM3, pressureKpa, gasDetected, valveOpen |
| Tariff:PricePerM3, Tariff:Currency | — | 2.50, PEN |
| Database:MigrateOnStartup | — | Enabled in Development |

## Database migrations

EF Core migrations live in src/Smartgn.Monitoring/Infrastructure/Persistence/Migrations. To add one, run:

dotnet ef migrations add MigrationName --project src/Smartgn.Monitoring/Smartgn.Monitoring.csproj --output-dir Infrastructure/Persistence/Migrations

## OpenAPI export and verification

With the service running, use ./scripts/export-openapi.ps1 to refresh docs/openapi.json and ./scripts/verify-openapi.ps1 to check metadata, tags, operations, security and success responses against tools/service.json.

## Test coverage

The xUnit tests use in-memory doubles and do not require Redis, PostgreSQL or external services. They cover consumption calculations and estimates, validation, query/report services, cache behavior and account isolation, Telemetry pagination and metrics, IAM client behavior and JWT authentication.
