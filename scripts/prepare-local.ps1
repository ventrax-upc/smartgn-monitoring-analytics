$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath (Split-Path -Parent $PSScriptRoot)

if (-not (Test-Path -LiteralPath '.env')) {
    Copy-Item -LiteralPath '.env.example' -Destination '.env'
}

$requiredNames = @('DB_PASSWORD', 'JWT_SECRET', 'INTERNAL_SERVICE_TOKEN')
$values = @{}
$lines = @(Get-Content -LiteralPath '.env')
foreach ($line in $lines) {
    if ($line -match '^([A-Z_]+)=(.*)$') {
        $values[$Matches[1]] = $Matches[2]
    }
}

foreach ($name in $requiredNames) {
    if (-not $values.ContainsKey($name) -or
        [string]::IsNullOrWhiteSpace($values[$name]) -or
        $values[$name].StartsWith('replace-', [System.StringComparison]::OrdinalIgnoreCase)) {
        $values[$name] = [Convert]::ToHexString(
            [Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
    }
}

$seen = @{}
$updatedLines = foreach ($line in $lines) {
    if ($line -match '^([A-Z_]+)=(.*)$' -and $values.ContainsKey($Matches[1])) {
        $name = $Matches[1]
        $seen[$name] = $true
        "$name=$($values[$name])"
    }
    else {
        $line
    }
}

foreach ($name in $requiredNames) {
    if (-not $seen.ContainsKey($name)) {
        $updatedLines += "$name=$($values[$name])"
    }
}

Set-Content -LiteralPath '.env' -Value $updatedLines -Encoding utf8
Write-Output 'Local configuration prepared without replacing existing values.'
