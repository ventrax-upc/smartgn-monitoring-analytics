# SmartGN Monitoring & Analytics

Consultas autorizadas de consumo de gas: lectura en vivo, consumo consolidado de varios medidores, gasto estimado, estimación mensual y reportes Pro.

C# / .NET 10, ASP.NET Core, PostgreSQL (EF Core), Redis (Cache-Aside) y gRPC privado hacia Telemetry & Data Ingestion y Payments & Billing. Este bounded context es dueño de su propia base de datos y no lee tablas de otros servicios.

## Endpoints (REST, detrás del API Gateway)

Todos requieren el JWT de IAM (`Authorization: Bearer ...`). La cuenta se toma del token, nunca de un parámetro del cliente.

| Método y ruta | Qué hace | Permiso IAM por medidor | Caché |
| --- | --- | --- | --- |
| `GET /api/v1/analytics/live?meterId={id}` | Última lectura: volumen, presión, gas, válvula y frescura (`current`, `delayed`, `no_data`) | `VIEW_CONSUMPTION` | No (debe ser reciente) |
| `GET /api/v1/analytics/consumption?meterIds={ids}&from=&to=` | Consumo por medidor (diferencia de lecturas acumuladas), gasto estimado y estimación mensual. Por defecto, el mes actual | `VIEW_CONSUMPTION` (periodo reciente) o `VIEW_HISTORY` (histórico) | Sí |
| `GET /api/v1/analytics/reports?meterIds={ids}&from=&to=` | Reporte consolidado guardado en PostgreSQL | `USE_REPORTS` + plan Pro (Billing gRPC) | Sí |
| `GET /health` | `UP` | — | — |

Respuestas de error (ProblemDetails): `400` datos inválidos (máx. 100 medidores, periodo máx. 31 días), `401` token inválido, `403` sin permiso sobre un medidor o sin plan Pro, `503` IAM, Billing o Telemetry no respondieron (nunca se asume Free ni "permitido").

## Comunicación interna

| Dependencia | Protocolo | Contrato |
| --- | --- | --- |
| Telemetry & Data Ingestion | gRPC `smartgn.telemetry.v1.TelemetryQueryService` (`GetReadingsBatch`, `GetLatestReadings`) | `com.ventrax.monitoring/Protos/telemetry.proto`, copia de [telemetry_query.proto](https://github.com/ventrax-upc/smartgn-telemetry-data-ingestion/blob/main/src/Smartgn.Telemetry/Protos/telemetry_query.proto) |
| Payments & Billing | gRPC `smartgn.billing.v1.SubscriptionEntitlementService.GetProEntitlement` | `com.ventrax.monitoring/Protos/billing.proto` (Billing debe publicar este servidor) |
| IAM | HTTP `POST /internal/v1/authorizations/check` | `{accountId, meterId, function, requiresPro}` → `{allowed}` |

Las llamadas internas envían `Authorization: Bearer {INTERNAL_SERVICE_TOKEN}` y tienen un plazo máximo (`Integration:RequestTimeoutSeconds`). Telemetry devuelve como máximo 1000 lecturas por dispositivo; el cliente pide las páginas anteriores cuando hace falta.

## Monitoring Cache (Redis, Cache-Aside)

1. Se verifican permisos (IAM) y Pro (Billing) **antes** de mirar la caché.
2. Se busca en Redis con una clave por tipo de consulta, cuenta, medidores y periodo (`analytics:{tipo}:v2:{cuenta}:{medidores}:{desde}:{hasta}`).
3. Si no está (MISS), se calcula desde Telemetry, se guarda en Redis con TTL y se responde.
4. TTL: 30 s si el periodo incluye datos recientes y 600 s si es histórico (sección `Cache`).
5. Si Redis se cae, la consulta se calcula igual desde la fuente.

Monitoreo de la caché:

- Header `X-Cache: HIT | MISS` en `/consumption` y `/reports`.
- Contadores `monitoring_cache_hits`, `monitoring_cache_misses` y `monitoring_cache_errors` del meter `SmartGN.Monitoring.Cache`:

```bash
dotnet-counters monitor -n com.ventrax.monitoring --counters SmartGN.Monitoring.Cache
```

## Configuración

| Clave (appsettings) | Variable de entorno | Por defecto |
| --- | --- | --- |
| `ConnectionStrings:DefaultConnection` | `ConnectionStrings__DefaultConnection` | PostgreSQL local `smartgn_monitoring` |
| `ConnectionStrings:RedisConnection` | `ConnectionStrings__RedisConnection` | `localhost:6379` |
| `Jwt:Secret` | `JWT_SECRET` (el mismo de IAM, mínimo 32 bytes) | **obligatorio** |
| `Integration:InternalServiceToken` | `INTERNAL_SERVICE_TOKEN` | vacío (desarrollo local) |
| `Integration:IamBaseUrl` | `IAM_BASE_URL` | `http://localhost:8080` |
| `GrpcServers:Telemetry` | `TELEMETRY_GRPC_URL` | `http://localhost:9084` |
| `GrpcServers:Billing` | `BILLING_GRPC_URL` | `http://localhost:9083` |
| `TelemetryMetrics:*` | — | `totalVolumeM3`, `pressureKpa`, `gasDetected`, `valveOpen` |
| `Tariff:PricePerM3`, `Tariff:Currency` | — | `2.50`, `PEN` |
| `Database:MigrateOnStartup` | — | `true` en Development |

## Ejecutar

```bash
dotnet run --project com.ventrax.monitoring
```

Swagger: `http://localhost:5198/swagger` (perfil Development). Con Docker:

```bash
docker build -t smartgn-monitoring-analytics .
```

```bash
docker run -p 8086:8086 -e JWT_SECRET=... -e ConnectionStrings__DefaultConnection=... smartgn-monitoring-analytics
```

## Base de datos

Migraciones EF Core en `com.ventrax.monitoring/Infrastructure/Persistence/Migrations`. Nueva migración:

```bash
dotnet ef migrations add NombreMigracion --project com.ventrax.monitoring --output-dir Infrastructure/Persistence/Migrations
```

## Pruebas

```bash
dotnet test MonitoringAnalytics.slnx
```

xUnit con dobles en memoria (sin Redis, PostgreSQL ni servicios externos): cálculo de consumo, estimaciones, validaciones, endpoints de consulta, reportes Pro, caché (HIT/MISS, Redis caído, aislamiento por cuenta), cliente gRPC de Telemetry (paginación y métricas), cliente IAM y JWT.
