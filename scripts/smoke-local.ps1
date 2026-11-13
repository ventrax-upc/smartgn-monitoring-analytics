param([string]$BaseUrl = 'http://127.0.0.1:8084')
$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath (Split-Path -Parent $PSScriptRoot)

Write-Output '1/4 Liveness'
$health = Invoke-WebRequest -UseBasicParsing -TimeoutSec 5 "$BaseUrl/health"
if ($health.Content -notmatch 'UP') {
    throw "/health did not answer UP: $($health.Content)"
}

Write-Output '2/4 Readiness'
$ready = Invoke-RestMethod -TimeoutSec 5 "$BaseUrl/ready"
if ($ready.status -ne 'UP' -or $ready.postgres -ne 'UP') {
    throw '/ready reported an unexpected state.'
}

Write-Output '3/4 Spring-compatible Swagger UI'
$ui = Invoke-WebRequest -UseBasicParsing -TimeoutSec 10 "$BaseUrl/swagger-ui.html"
if ($ui.StatusCode -ne 200) {
    throw "Swagger UI returned HTTP $($ui.StatusCode)."
}

Write-Output '4/4 OpenAPI document'
$api = Invoke-RestMethod -TimeoutSec 10 "$BaseUrl/v3/api-docs"
if ($api.info.title -ne 'SmartGN - Monitoring & Analytics' -or $api.info.version -ne 'v1') {
    throw 'OpenAPI metadata does not match Monitoring v1.'
}
foreach ($path in @(
    '/api/v1/analytics/live',
    '/api/v1/analytics/consumption',
    '/api/v1/analytics/reports')) {
    if ($path -notin $api.paths.PSObject.Properties.Name) {
        throw "OpenAPI is missing $path."
    }
}
if (-not $api.components.securitySchemes.bearerAuth -or
    -not $api.components.securitySchemes.serviceAuth) {
    throw 'OpenAPI is missing the shared bearerAuth/serviceAuth schemes.'
}

Write-Output 'Smoke OK: liveness, PostgreSQL readiness, Swagger UI and OpenAPI.'
