# REST and integration contracts

Public REST endpoints are intended to be exposed through the API Gateway. The account is derived from the IAM JWT and is never accepted as a client-supplied query parameter. /health is anonymous for infrastructure probes.

| Method and route | Purpose | Per-meter authorization | Cache |
| --- | --- | --- | --- |
| GET /api/v1/analytics/live?meterId={id} | Latest volume, pressure, gas, valve and freshness (current, delayed, no_data) | VIEW_CONSUMPTION | No |
| GET /api/v1/analytics/consumption?meterIds={ids}&from=&to= | Per-meter consumption, expense estimate and monthly estimate; defaults to the current month | VIEW_CONSUMPTION for recent periods or VIEW_HISTORY for history | Yes |
| GET /api/v1/analytics/reports?meterIds={ids}&from=&to= | Consolidated report stored in PostgreSQL | USE_REPORTS and Pro entitlement | Yes |
| GET /health | Service liveness response UP | Anonymous | No |

Requests accept at most 100 meters and a period of at most 31 days. Errors use ProblemDetails: 400 for invalid input, 401 for an invalid token, 403 for missing meter permission or Pro entitlement, and 503 when IAM, Billing or Telemetry is unavailable.

## Internal dependencies

| Dependency | Protocol and operation | Local contract |
| --- | --- | --- |
| Telemetry & Data Ingestion | gRPC smartgn.telemetry.v1.TelemetryQueryService: GetReadingsBatch, GetLatestReadings. Monitoring sends supply point (meter) identifiers. Telemetry resolves readings against the versioned association ledger and the reading timestamp, so a meter replacement preserves history. | src/Smartgn.Monitoring/Protos/telemetry.proto, copied from the Telemetry service query contract |
| Payments & Billing | gRPC smartgn.billing.v1.SubscriptionEntitlementService.GetProEntitlement | src/Smartgn.Monitoring/Protos/billing.proto; Billing must expose this server contract |
| IAM | HTTP POST /internal/v1/authorizations/check | Request contains accountId, meterId, function and requiresPro; response contains allowed |

Internal calls send Authorization: Bearer {INTERNAL_SERVICE_TOKEN} and use Integration:RequestTimeoutSeconds. Telemetry pages at most 1,000 readings per device; the client requests earlier pages when needed.

## Cache behavior

Redis keys include query type, account, meters and period: analytics:{type}:v2:{account}:{meters}:{from}:{to}. Recent data uses a 30-second TTL; historical data uses 600 seconds. Consumption and report responses include X-Cache: HIT | MISS. Cache hit, miss and error counters are emitted by SmartGN.Monitoring.Cache.
