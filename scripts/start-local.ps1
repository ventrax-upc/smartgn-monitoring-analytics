param([switch]$SkipBuild)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location -LiteralPath $repoRoot

& ./scripts/prepare-local.ps1
$arguments = @('compose', '--env-file', '.env', '-f', 'compose.yml', 'up', '-d', '--wait')
if (-not $SkipBuild) {
    $arguments += '--build'
}

& docker @arguments
if ($LASTEXITCODE -ne 0) {
    throw 'Monitoring local startup failed.'
}

Write-Output 'Monitoring: http://127.0.0.1:8084'
Write-Output 'Swagger:    http://127.0.0.1:8084/swagger-ui.html'
Write-Output 'OpenAPI:    http://127.0.0.1:8084/v3/api-docs'
