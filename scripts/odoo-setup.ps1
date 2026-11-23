# Prepares Odoo 20 for the POS (BACKEND computer). Safe to run several times.
#  1. Creates docker/.env with a random PostgreSQL password (first time only).
#  2. Starts Odoo + PostgreSQL with Docker.
#  3. Creates the "pos" database with point_of_sale (first time only).
#  4. Runs docker/setup/setup_pos.py: tax, products, cash method, POS, session, integration user.
#  5. Stores the Odoo API key and POS id in dotnet user-secrets (never in Git).
$ErrorActionPreference = 'Continue'  # native tools write progress to stderr
$root = Split-Path $PSScriptRoot -Parent
$docker = Join-Path $root 'docker'
$api = Join-Path $root 'backend\src\Pos.Api'

$envFile = Join-Path $docker '.env'
if (-not (Test-Path $envFile)) {
    $bytes = New-Object byte[] 18
    [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
    $pwd = [Convert]::ToBase64String($bytes) -replace '[+/=]', 'x'
    "POSTGRES_PASSWORD=$pwd" | Out-File -Encoding ascii $envFile
    Write-Host "Creado docker/.env"
}
$dbPassword = ((Get-Content $envFile | Where-Object { $_ -like 'POSTGRES_PASSWORD=*' }) -split '=', 2)[1]

$compose = @('compose', '-f', (Join-Path $docker 'docker-compose.yml'), '--project-directory', $docker)

# Docker must be running before anything else.
docker info --format '{{.ServerVersion}}' *> $null
if ($LASTEXITCODE -ne 0) {
    throw "Docker no responde. Abra Docker Desktop, espere a 'Engine running' y vuelva a ejecutar este script."
}

# The images need ~4 GB once extracted; Docker stores them on the system drive by default.
$systemDrive = (Get-PSDrive ($env:SystemDrive.TrimEnd(':')))
$freeGb = [math]::Round($systemDrive.Free / 1GB, 1)
if ($freeGb -lt 10) {
    Write-Host "AVISO: solo hay $freeGb GB libres en $($env:SystemDrive). Docker necesita unos 10 GB para Odoo y PostgreSQL." -ForegroundColor Yellow
}

# Download the images first, with retries: on slow or unstable connections a
# layer can fail ("failed to register layer ... input/output error") and a
# second attempt reuses the layers that were already downloaded.
Write-Host "Descargando las imágenes de Odoo y PostgreSQL (la primera vez ~1 GB, puede tardar)..."
$pulled = $false
for ($attempt = 1; $attempt -le 3 -and -not $pulled; $attempt++) {
    docker @compose pull
    if ($LASTEXITCODE -eq 0) { $pulled = $true }
    elseif ($attempt -lt 3) {
        Write-Host "La descarga falló (intento $attempt de 3). Reintentando en 10 s..." -ForegroundColor Yellow
        Start-Sleep -Seconds 10
    }
}
if (-not $pulled) {
    Write-Host ""
    Write-Host "No se pudieron descargar o descomprimir las imágenes de Docker. Pruebe en este orden:" -ForegroundColor Red
    Write-Host "  1. Libere espacio en $($env:SystemDrive) (al menos 10 GB; ahora hay $freeGb GB)."
    Write-Host "  2. Cierre Docker Desktop, ejecute 'wsl --shutdown', ábralo de nuevo y repita este script."
    Write-Host "  3. Docker Desktop > Troubleshoot (icono de insecto) > 'Clean / Purge data' y repita."
    Write-Host "  4. Agregue la carpeta de Docker como exclusión del antivirus o desactívelo durante la descarga."
    Write-Host "  Detalle en INSTALACION.md, sección 'Problemas frecuentes'."
    throw "Falló la descarga de las imágenes de Docker (no es un error del proyecto)."
}

Write-Host "Iniciando Odoo y PostgreSQL..."
docker @compose up -d
if ($LASTEXITCODE -ne 0) { throw "docker compose no pudo iniciar los contenedores. Revise el mensaje anterior de Docker." }

$dbArgs = "--db_host=db --db_user=odoo --db_password=$dbPassword"

$exists = docker exec pos-odoo-db psql -U odoo -d postgres -tAc "SELECT 1 FROM pg_database WHERE datname='pos'"
if ($exists -ne '1') {
    Write-Host "Creando la base 'pos' con point_of_sale (tarda unos minutos)..."
    docker exec pos-odoo-web bash -c "odoo $dbArgs -d pos -i point_of_sale,stock --without-demo=True --stop-after-init --no-http --log-level=warn"
    if ($LASTEXITCODE -ne 0) { throw "No se pudo crear la base de Odoo." }
}

Write-Host "Configurando productos, impuestos, POS, sesión y usuario de integración..."
docker cp (Join-Path $docker 'setup\setup_pos.py') pos-odoo-web:/tmp/setup_pos.py | Out-Null
$output = docker exec pos-odoo-web bash -c "odoo shell $dbArgs -d pos --no-http --log-level=error < /tmp/setup_pos.py"
$line = $output | Where-Object { $_ -like 'SETUP_RESULT=*' } | Select-Object -Last 1
if (-not $line) { $output | Select-Object -Last 15; throw "El script de configuración de Odoo falló." }
$result = $line.Substring('SETUP_RESULT='.Length) | ConvertFrom-Json

Push-Location $api
try {
    dotnet user-secrets set "Odoo:ApiKey" $result.api_key | Out-Null
    dotnet user-secrets set "Odoo:PosConfigId" "$($result.pos_config_id)" | Out-Null
    $hasJwt = dotnet user-secrets list | Select-String 'Jwt:SigningKey'
    if (-not $hasJwt) {
        $bytes = New-Object byte[] 48
        [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
        dotnet user-secrets set "Jwt:SigningKey" ([Convert]::ToBase64String($bytes)) | Out-Null
    }
} finally { Pop-Location }

Write-Host ""
Write-Host "Odoo listo:" -ForegroundColor Green
Write-Host "  POS:      $($result.pos_config_id)  sesión $($result.pos_session_name) ($($result.pos_session_state))"
Write-Host "  Efectivo: método de pago $($result.cash_payment_method_id)"
Write-Host "  Productos: $($result.products | ConvertTo-Json -Compress)"
Write-Host "  API key guardada en user-secrets (no en el repositorio)."
Write-Host "Cajeros: créelos en Odoo (Ajustes > Usuarios) con permiso 'Punto de venta / Usuario'."
