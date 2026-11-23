# FRONTEND computer: points the Blazor app to the API of the backend computer
# and serves it on port 5200. Open http://localhost:5200 in this computer's browser.
param(
    [Parameter(Mandatory)] [string] $ApiUrl,   # e.g. http://192.168.1.20:5100
    [int] $Port = 5200
)
$ErrorActionPreference = 'Continue'
$root = Split-Path $PSScriptRoot -Parent
$web = Join-Path $root 'frontend\src\Pos.Frontend.Web'

$ApiUrl = $ApiUrl.TrimEnd('/')
try {
    $health = Invoke-RestMethod "$ApiUrl/health" -TimeoutSec 5
    Write-Host "API alcanzable en $ApiUrl ($($health.status))." -ForegroundColor Green
} catch {
    Write-Host "AVISO: no se pudo contactar $ApiUrl/health. Revise la IP, que la API esté corriendo y el firewall del backend." -ForegroundColor Yellow
}

# Runtime configuration read by the browser (no recompilation needed).
@{ ApiBaseUrl = $ApiUrl } | ConvertTo-Json | Out-File -Encoding utf8 (Join-Path $web 'wwwroot\appsettings.json')
Write-Host "Frontend configurado para usar $ApiUrl"
Write-Host "Abra en este navegador: http://localhost:$Port" -ForegroundColor Green

Set-Location $web
dotnet run --urls "http://0.0.0.0:$Port"
