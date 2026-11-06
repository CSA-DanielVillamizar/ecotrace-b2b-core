<#
.SYNOPSIS
  Levanta la producción simulada de Identity (ADR 0006): otro puerto, otro emisor, otra clave y otra base.

.DESCRIPTION
  "Producción" en el Trabajo 3 es un segundo conjunto de servicios con otra configuración (especificación, Despliegue).
  Este script levanta el de Identity en http://localhost:6101 con ASPNETCORE_ENVIRONMENT=Production:

  - Emisor: https://identity.ecotrace.local/produccion (appsettings.Production.json).
  - Base: App_Data/identity-produccion.db, distinta de la de desarrollo.
  - Clave de firma, contraseñas y secretos de servicio: se generan la primera vez en
    %LOCALAPPDATA%\EcoTrace\produccion (fuera del repositorio) y se pasan por variables de entorno.

  Los demás escuadrones agregan su servicio a $servicios. Ctrl+C detiene todo.
#>
param(
  [string] $Secretos = (Join-Path $env:LOCALAPPDATA 'EcoTrace\produccion')
)
$ErrorActionPreference = 'Stop'
$raiz = Split-Path -Parent $PSScriptRoot
Set-Location $raiz

function Nuevo-Secreto([int] $bytes = 24) {
  $datos = New-Object byte[] $bytes
  [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($datos)
  ([Convert]::ToBase64String($datos)).Replace('+', 'A').Replace('/', 'B').TrimEnd('=')
}

function Sha256Hex([string] $texto) {
  $hash = [Security.Cryptography.SHA256]::Create().ComputeHash([Text.Encoding]::UTF8.GetBytes($texto))
  ([BitConverter]::ToString($hash)).Replace('-', '')
}

New-Item -ItemType Directory -Force $Secretos | Out-Null
$archivoSecretos = Join-Path $Secretos 'secretos.json'
if (-not (Test-Path $archivoSecretos)) {
  Write-Host 'Generando los secretos de producción (solo la primera vez)...' -ForegroundColor Cyan
  @{
    adminEmail        = 'admin@ecotrace.co'
    adminPassword     = 'Prod-' + (Nuevo-Secreto 18)
    conductorEmail    = 'conductor.verificacion@ecotrace.co'
    conductorPassword = 'Prod-' + (Nuevo-Secreto 18)
    secretoBilling    = Nuevo-Secreto
    secretoCargo      = Nuevo-Secreto
  } | ConvertTo-Json | Set-Content $archivoSecretos -Encoding UTF8
}
$s = Get-Content $archivoSecretos -Raw | ConvertFrom-Json

$claveJwt = Join-Path $Secretos 'identity-jwt.pem'
if (-not (Test-Path $claveJwt)) {
  Write-Host 'Generando la clave de firma de producción (RSA 2048)...' -ForegroundColor Cyan
  $guion = [IO.Path]::Combine([IO.Path]::GetTempPath(), "ecotrace-clave-$([Guid]::NewGuid().ToString('N')).fsx")
  Set-Content $guion "System.IO.File.WriteAllText(@`"$claveJwt`", System.Security.Cryptography.RSA.Create(2048).ExportPkcs8PrivateKeyPem())"
  dotnet fsi $guion
  Remove-Item $guion
  if (-not (Test-Path $claveJwt)) { throw 'No se pudo generar la clave de firma.' }
}

Write-Host 'Compilando en Release...' -ForegroundColor Cyan
dotnet build src/EcoTrace.Identity/Api -c Release --nologo --verbosity quiet
if ($LASTEXITCODE -ne 0) { throw 'La compilación falló.' }

$env:ASPNETCORE_ENVIRONMENT = 'Production'
$env:Jwt__ClavePrivadaArchivo = $claveJwt
$env:Identity__Operador__AdminPassword = $s.adminPassword
$env:Identity__Verificacion__ConductorPassword = $s.conductorPassword
$env:Identity__ClientesServicio__0__SecretoSha256 = Sha256Hex $s.secretoBilling
$env:Identity__ClientesServicio__1__SecretoSha256 = Sha256Hex $s.secretoCargo

# Base propia de producción: los entornos no comparten datos.
$datosIdentity = Join-Path $raiz 'src/EcoTrace.Identity/Api/App_Data'
New-Item -ItemType Directory -Force $datosIdentity | Out-Null
$env:ConnectionStrings__Default = "Data Source=$(Join-Path $datosIdentity 'identity-produccion.db')"

$servicios = @(
  @{ Nombre = 'Identity'; Proyecto = 'src/EcoTrace.Identity/Api'; Url = 'http://localhost:6101' }
)

$procesos = foreach ($servicio in $servicios) {
  Start-Process dotnet -NoNewWindow -PassThru -ArgumentList @(
    'run', '-c', 'Release', '--no-build', '--no-launch-profile', '--project', $servicio.Proyecto, '--urls', $servicio.Url)
}

Write-Host ''
Write-Host 'Producción simulada: Identity en http://localhost:6101' -ForegroundColor Green
Write-Host "Secretos (fuera del repositorio): $archivoSecretos"
Write-Host 'Verificación: scripts\verificar-release.ps1'
Write-Host 'Ctrl+C detiene todo.'

try {
  Wait-Process -Id ($procesos | ForEach-Object Id)
}
finally {
  $procesos | Where-Object { -not $_.HasExited } | Stop-Process -Force -ErrorAction SilentlyContinue
}
