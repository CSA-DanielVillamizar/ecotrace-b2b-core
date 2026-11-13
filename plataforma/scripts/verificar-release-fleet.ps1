<#
.SYNOPSIS
  Verificación del release de Fleet Management en dos capas (ADR 0006). Termina con código distinto de cero si
  falla un paso que bloquea.

.DESCRIPTION
  Capa 1, configuración: la producción de Fleet tiene su propio emisor, toma la llave del Identity de producción,
  usa otra base y no expone Swagger; la app de producción apunta al Fleet de producción.
  Capa 2, comportamiento real: se le pregunta al Fleet que responde. Tiene que decir "produccion", tener el
  vehículo conocido PRD001 y no duplicar una novedad que se repite con la misma Idempotency-Key.

  Usa la producción simulada: Identity con scripts\levantar-produccion.ps1 y Fleet con su perfil "produccion":
  dotnet run -c Release --project src/EcoTrace.FleetManagement/Api --launch-profile produccion

  Son los pasos de Fleet para verificar-release.ps1; quedan aparte mientras ese script no esté en main.

.EXAMPLE
  scripts\verificar-release-fleet.ps1                              # contra producción (Fleet en 6102)
  scripts\verificar-release-fleet.ps1 -Fleet http://localhost:5102  # contra desarrollo: debe bloquear
#>
param(
  [string] $Fleet = 'http://localhost:6102',
  [string] $Identity = 'http://localhost:6101',
  [string] $IdentityDesarrollo = 'http://localhost:5101',
  [string] $Secretos = (Join-Path $env:LOCALAPPDATA 'EcoTrace\produccion\secretos.json')
)
$ErrorActionPreference = 'Stop'
$raiz = Split-Path -Parent $PSScriptRoot

$EmisorProduccion = 'https://identity.ecotrace.local/produccion'
$VehiculoConocido = 'f1ee7000-0000-4000-8000-000000000001'
# Siempre la misma clave: repetir la verificación no llena producción de novedades (desde la segunda vez es un 200).
$ClaveVerificacion = 'f1ee7000-0000-4000-8000-0000000000aa'
$resultados = New-Object System.Collections.Generic.List[object]

function Paso([string] $capa, [string] $nombre, [bool] $bloquea, [scriptblock] $prueba) {
  try {
    $detalle = & $prueba
    $resultados.Add([pscustomobject]@{ Capa = $capa; Paso = $nombre; Bloquea = $bloquea; Resultado = 'Cumple'; Detalle = "$detalle" })
  }
  catch {
    $resultados.Add([pscustomobject]@{ Capa = $capa; Paso = $nombre; Bloquea = $bloquea; Resultado = 'FALLA'; Detalle = $_.Exception.Message })
  }
}

function Exigir([bool] $condicion, [string] $mensaje) { if (-not $condicion) { throw $mensaje } }

function Payload([string] $jwt) {
  $parte = $jwt.Split('.')[1].Replace('-', '+').Replace('_', '/')
  switch ($parte.Length % 4) { 2 { $parte += '==' } 3 { $parte += '=' } }
  [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($parte)) | ConvertFrom-Json
}

function Login([string] $base, [string] $email, [string] $password) {
  Invoke-RestMethod -Method Post "$base/api/auth/login" -ContentType 'application/json' -TimeoutSec 10 `
    -Body (@{ email = $email; password = $password } | ConvertTo-Json)
}

# ---------------------------------------------------------------- Capa 1: configuración

$fleetProd = Join-Path $raiz 'src/EcoTrace.FleetManagement/Api/appsettings.Production.json'
$appProd = Join-Path $raiz 'src/EcoTrace.App/Configuracion/appsettings.Produccion.json'

Paso 'Capa 1' 'Fleet en producción: emisor propio, llave de Identity de producción, otra base y sin Swagger' $true {
  $c = Get-Content $fleetProd -Raw | ConvertFrom-Json
  Exigir ($c.Jwt.Emisor -eq $EmisorProduccion) "Emisor = '$($c.Jwt.Emisor)'"
  Exigir ($c.Jwt.UrlJwks -notmatch ':510\d') "El JWKS apunta a desarrollo: $($c.Jwt.UrlJwks)"
  Exigir ($c.Database.Archivo -and $c.Database.Archivo -ne 'fleet.db') "Base = '$($c.Database.Archivo)'"
  Exigir ($c.Swagger.Habilitado -eq $false) 'Swagger está habilitado en producción.'
  "iss=$($c.Jwt.Emisor), base=$($c.Database.Archivo)"
}

Paso 'Capa 1' 'Fleet en producción siembra el vehículo conocido' $true {
  $c = Get-Content $fleetProd -Raw | ConvertFrom-Json
  Exigir ($c.Semilla.VehiculoConocido.VehiculoId -eq $VehiculoConocido) 'Falta el vehículo conocido en la configuración.'
  "placa $($c.Semilla.VehiculoConocido.Placa) ($VehiculoConocido)"
}

Paso 'Capa 1' 'La app de producción apunta al Fleet de producción' $true {
  $c = Get-Content $appProd -Raw | ConvertFrom-Json
  Exigir ($c.entorno -eq 'produccion') "entorno = '$($c.entorno)'"
  Exigir ($c.servicios.fleet -notmatch ':510\d') "Fleet apunta al puerto de desarrollo: $($c.servicios.fleet)"
  "fleet=$($c.servicios.fleet)"
}

# ---------------------------------------------------------------- Capa 2: comportamiento real

Paso 'Capa 2' "GET $Fleet/api/_meta/entorno responde produccion" $true {
  $meta = Invoke-RestMethod "$Fleet/api/_meta/entorno" -TimeoutSec 10
  Exigir ($meta.entorno -eq 'produccion') "El Fleet que responde es de '$($meta.entorno)'"
  "servicio=$($meta.servicio), entorno=$($meta.entorno), version=$($meta.version)"
}

$s = if (Test-Path $Secretos) { Get-Content $Secretos -Raw | ConvertFrom-Json } else { $null }
$token = $null

Paso 'Capa 2' 'Identity de producción emite el token con el emisor de producción' $true {
  Exigir ($null -ne $s) "No están los secretos de producción ($Secretos). Ejecute scripts\levantar-produccion.ps1."
  $script:token = (Login $Identity $s.adminEmail $s.adminPassword).accessToken
  $p = Payload $script:token
  Exigir ($p.iss -eq $EmisorProduccion) "iss = '$($p.iss)'"
  "iss=$($p.iss)"
}

Paso 'Capa 2' 'El vehículo conocido existe en el Fleet que responde' $true {
  Exigir ($null -ne $script:token) 'Sin token de producción.'
  $vehiculo = Invoke-RestMethod "$Fleet/api/vehiculos/$VehiculoConocido" -Headers @{ Authorization = "Bearer $script:token" } -TimeoutSec 10
  Exigir ($vehiculo.placa -eq 'PRD001') "Placa = '$($vehiculo.placa)'"
  "placa=$($vehiculo.placa), estado=$($vehiculo.estado)"
}

Paso 'Capa 2' 'Repetir la novedad con la misma Idempotency-Key no la duplica' $true {
  Exigir ($null -ne $script:token) 'Sin token de producción.'
  $ruta = "$Fleet/api/vehiculos/$VehiculoConocido/novedades"
  $autorizacion = @{ Authorization = "Bearer $script:token" }
  $encabezados = @{ Authorization = "Bearer $script:token"; 'Idempotency-Key' = $ClaveVerificacion }
  $cuerpo = @{ tipo = 'Otra'; descripcion = 'Novedad de verificacion del release'; ocurridaEn = (Get-Date).ToUniversalTime().ToString('o') } | ConvertTo-Json

  $primera = Invoke-RestMethod -Method Post $ruta -Headers $encabezados -ContentType 'application/json' -Body $cuerpo -TimeoutSec 10
  $antes = @(Invoke-RestMethod $ruta -Headers $autorizacion -TimeoutSec 10).Count
  $reintento = Invoke-WebRequest -UseBasicParsing -Method Post $ruta -Headers $encabezados -ContentType 'application/json' -Body $cuerpo -TimeoutSec 10
  $despues = @(Invoke-RestMethod $ruta -Headers $autorizacion -TimeoutSec 10).Count
  $segunda = $reintento.Content | ConvertFrom-Json

  Exigir ([int] $reintento.StatusCode -eq 200) "El reintento respondió $([int] $reintento.StatusCode), no 200"
  Exigir ($segunda.novedadId -eq $primera.novedadId) 'El reintento devolvió otra novedad.'
  Exigir ($antes -eq $despues) "Había $antes novedades y quedaron $despues"
  "reintento 200, misma novedad $($primera.novedadId), $despues novedad(es) antes y después"
}

Paso 'Capa 2' 'Un token de desarrollo no sirve en el Fleet de producción' $false {
  try { $dev = Login $IdentityDesarrollo 'admin@ecotrace.local' 'EcoTrace-Admin-Dev-2026' }
  catch { return "Omitido: desarrollo no responde en $IdentityDesarrollo" }
  $codigo = 0
  try {
    Invoke-RestMethod "$Fleet/api/vehiculos" -Headers @{ Authorization = "Bearer $($dev.accessToken)" } -TimeoutSec 10 | Out-Null
    $codigo = 200
  }
  catch { $codigo = [int] $_.Exception.Response.StatusCode }
  Exigir ($codigo -eq 401) "El Fleet que responde aceptó un token de desarrollo ($codigo)"
  '401 (otro emisor y otra llave)'
}

# ---------------------------------------------------------------- Resultado

$resultados | Format-Table Capa, Paso, Bloquea, Resultado, Detalle -AutoSize -Wrap | Out-String -Width 220 | Write-Host
$bloqueantes = @($resultados | Where-Object { $_.Bloquea -and $_.Resultado -ne 'Cumple' })
if ($bloqueantes.Count -gt 0) {
  Write-Host "RELEASE DE FLEET BLOQUEADO: $($bloqueantes.Count) paso(s) bloqueante(s) fallaron." -ForegroundColor Red
  exit 1
}

Write-Host "Release de Fleet aprobado: $($resultados.Count) pasos, ninguno bloqueante falló." -ForegroundColor Green
exit 0
