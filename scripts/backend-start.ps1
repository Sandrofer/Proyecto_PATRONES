# BACKEND computer: starts Odoo (Docker) and the .NET API on port 5100,
# reachable from the frontend computer through the local network.
param([int] $Port = 5100)
$ErrorActionPreference = 'Continue'  # native tools write progress to stderr
$root = Split-Path $PSScriptRoot -Parent

Write-Host "Iniciando Odoo y PostgreSQL (Docker)..."
docker compose -f (Join-Path $root 'docker\docker-compose.yml') --project-directory (Join-Path $root 'docker') up -d
if ($LASTEXITCODE -ne 0) { Write-Host "Docker no respondió. Abra Docker Desktop y reintente." -ForegroundColor Red; exit 1 }

# Windows Firewall: allow the frontend computer to reach the API (needs admin once).
$rule = Get-NetFirewallRule -DisplayName "POS API $Port" -ErrorAction SilentlyContinue
if (-not $rule) {
    $isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
    if ($isAdmin) {
        New-NetFirewallRule -DisplayName "POS API $Port" -Direction Inbound -Protocol TCP -LocalPort $Port -Action Allow -Profile Private,Domain | Out-Null
        Write-Host "Regla de firewall creada para el puerto $Port."
    } else {
        Write-Host "AVISO: ejecute UNA vez PowerShell como administrador y corra:" -ForegroundColor Yellow
        Write-Host "  New-NetFirewallRule -DisplayName 'POS API $Port' -Direction Inbound -Protocol TCP -LocalPort $Port -Action Allow -Profile Private,Domain" -ForegroundColor Yellow
    }
}

$ips = Get-NetIPAddress -AddressFamily IPv4 |
    Where-Object { $_.IPAddress -notlike '127.*' -and $_.IPAddress -notlike '169.254.*' -and $_.InterfaceAlias -notmatch 'vEthernet|WSL|Loopback' } |
    Select-Object -ExpandProperty IPAddress
Write-Host ""
Write-Host "En la computadora FRONTEND use una de estas direcciones:" -ForegroundColor Green
foreach ($ip in $ips) { Write-Host "  scripts\frontend-start.ps1 -ApiUrl http://${ip}:$Port" -ForegroundColor Green }
Write-Host ""

Set-Location (Join-Path $root 'backend')
dotnet run --project src\Pos.Api --urls "http://0.0.0.0:$Port"
