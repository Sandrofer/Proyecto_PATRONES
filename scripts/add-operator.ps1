# Adds (or replaces) a cashier who can log into the POS. Only the PBKDF2 hash
# of the password is stored, in dotnet user-secrets of the API.
param(
    [Parameter(Mandatory)] [string] $Username,
    [string] $DisplayName = $Username,
    [int] $Slot = 0
)
$ErrorActionPreference = 'Continue'  # native tools write progress to stderr
$api = Join-Path (Split-Path $PSScriptRoot -Parent) 'backend\src\Pos.Api'

$secure = Read-Host "Contraseña para $Username" -AsSecureString
$plain = [Runtime.InteropServices.Marshal]::PtrToStringAuto([Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure))

$salt = New-Object byte[] 16
[Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($salt)
$kdf = New-Object Security.Cryptography.Rfc2898DeriveBytes($plain, $salt, 100000, [Security.Cryptography.HashAlgorithmName]::SHA256)
$hash = 'PBKDF2$100000$' + [Convert]::ToBase64String($salt) + '$' + [Convert]::ToBase64String($kdf.GetBytes(32))

Push-Location $api
try {
    dotnet user-secrets set "Operators:Accounts:${Slot}:Username" $Username | Out-Null
    dotnet user-secrets set "Operators:Accounts:${Slot}:DisplayName" $DisplayName | Out-Null
    dotnet user-secrets set "Operators:Accounts:${Slot}:PasswordHash" $hash | Out-Null
} finally { Pop-Location }
Write-Host "Cajero '$Username' guardado (posición $Slot). Reinicie la API." -ForegroundColor Green
