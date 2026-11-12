$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath (Split-Path -Parent $PSScriptRoot)

dotnet test Smartgn.Monitoring.slnx -c Release
if ($LASTEXITCODE -ne 0) {
    throw 'Monitoring & Analytics test suite failed.'
}
