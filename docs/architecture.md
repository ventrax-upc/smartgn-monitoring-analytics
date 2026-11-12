# Architecture

Monitoring & Analytics owns the consumption-query and report bounded context. It persists reports in its own PostgreSQL database through EF Core and does not query another service's database.

The service follows a Domain, Application, Infrastructure and REST interface structure:

- Domain contains consumption calculations, expense estimates, aggregates and value objects.
- Application contains query/report use cases, rules, options, DTOs and outbound ports.
- Infrastructure implements PostgreSQL persistence, Redis caching, IAM authorization and gRPC clients.
- Interfaces/Rest exposes the public HTTP controller.

IAM authorizes each meter over HTTP. Telemetry provides readings and Billing provides Pro entitlement through private gRPC contracts. Redis uses Cache-Aside for consumption and report queries. Authorization and entitlement checks happen before cache reads; if Redis is unavailable, the service computes from Telemetry.

## Request flow

1. The service validates the IAM JWT and derives the account from its claims.
2. It checks the requested meter functions with IAM and checks Pro entitlement when the report endpoint requires it.
3. It checks Redis for an account-, meter- and period-specific result.
4. On a cache miss, it requests Telemetry readings, aggregates consumption and estimated expense, persists reports when applicable, and caches the result.
5. A Redis failure does not prevent the service from querying its source.

EF Core migrations are under Infrastructure/Persistence/Migrations.
