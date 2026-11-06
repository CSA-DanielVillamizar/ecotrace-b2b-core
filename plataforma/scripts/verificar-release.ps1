<#
.SYNOPSIS
  Verificación del release en dos capas (ADR 0006). Termina con código distinto de cero si falla un paso que bloquea.

.DESCRIPTION
  Capa 1, configuración: el build de Release de la app lleva la configuración de producción, y la de producción de
  Identity no apunta a desarrollo ni trae claves.
  Capa 2, comportamiento real: se le pregunta al servicio que responde. GET /api/_meta/entorno debe decir "produccion",
  el token debe traer el emisor de producción y el dato conocido de producción (la organización de verificación, con id
  fijo) tiene que existir. Si por error la URL apunta a desarrollo, el release no se aprueba aunque todo "funcione".

  Hoy cubre el módulo Identity. Cada escuadrón agrega sus pasos con Paso.

.EXAMPLE
  scripts\verificar-release.ps1                               # contra la producción simulada (http://localhost:6101)
  scripts\verificar-release.ps1 -Identity http://localhost:5101  # contra desarrollo: debe bloquear
#>
param(
  [string] $Identity = 'http://localhost:6101',
  [string] $IdentityDesarrollo = 'http://localhost:5101',
  [string] $Secretos = (Join-Path $env:LOCALAPPDATA 'EcoTrace\produccion\secretos.json')
)
$ErrorActionPreference = 'Stop'
$raiz = Split-Path -Parent $PSScriptRoot

$EmisorProduccion = 'https://identity.ecotrace.local/produccion'
$TenantVerificacion = '0e7ace00-0000-4000-8000-0000000000a1'
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

$appProd = Join-Path $raiz 'src/EcoTrace.App/Configuracion/appsettings.Produccion.json'
$appProyecto = Join-Path $raiz 'src/EcoTrace.App/EcoTrace.App.csproj'
$identityProd = Join-Path $raiz 'src/EcoTrace.Identity/Api/appsettings.Production.json'

Paso 'Capa 1' 'La app en Release incrusta appsettings.Produccion.json' $true {
  [xml] $csproj = Get-Content $appProyecto -Raw
  $grupo = $csproj.Project.ItemGroup | Where-Object { $_.Condition -match "'Release'" -and $_.Condition -match '==' }
  $recurso = $grupo.EmbeddedResource | Where-Object { $_.Include -match 'Produccion' -and $_.LogicalName -eq 'appsettings.json' }
  Exigir ($null -ne $recurso) 'El build de Release no incrusta la configuración de producción.'
  'Release -> Configuracion/appsettings.Produccion.json'
}

Paso 'Capa 1' 'La configuración de producción de la app no apunta a desarrollo' $true {
  $c = Get-Content $appProd -Raw | ConvertFrom-Json
  Exigir ($c.entorno -eq 'produccion') "entorno = '$($c.entorno)'"
  Exigir ($c.servicios.identity -notmatch ':510\d') "Identity apunta al puerto de desarrollo: $($c.servicios.identity)"
  "entorno=produccion, identity=$($c.servicios.identity)"
}

Paso 'Capa 1' 'Identity en producción: emisor propio, sin claves generadas ni Swagger' $true {
  $c = Get-Content $identityProd -Raw | ConvertFrom-Json
  Exigir ($c.Jwt.Emisor -eq $EmisorProduccion) "Emisor = '$($c.Jwt.Emisor)'"
  Exigir ($c.Jwt.GenerarClaveSiFalta -eq $false) 'GenerarClaveSiFalta no está en false.'
  Exigir ($c.Swagger.Habilitado -eq $false) 'Swagger está habilitado en producción.'
  "iss=$($c.Jwt.Emisor)"
}

Paso 'Capa 1' 'Ninguna clave privada ni secreto de producción en el repositorio' $true {
  Push-Location $raiz
  try {
    $claves = @(git grep -l -I -e 'BEGIN PRIVATE KEY' -e 'BEGIN RSA PRIVATE KEY' -- . 2>$null)
    Exigir ($claves.Count -eq 0) "Archivos con claves: $($claves -join ', ')"
    if (Test-Path $Secretos) {
      $s = Get-Content $Secretos -Raw | ConvertFrom-Json
      foreach ($valor in @($s.adminPassword, $s.conductorPassword, $s.secretoBilling, $s.secretoCargo)) {
        $hallado = @(git grep -l -F -e $valor -- . 2>$null)
        Exigir ($hallado.Count -eq 0) "Un secreto de producción aparece en: $($hallado -join ', ')"
      }
    }
    'Sin claves ni secretos de producción en archivos versionados'
  }
  finally { Pop-Location }
}

# ---------------------------------------------------------------- Capa 2: comportamiento real

Paso 'Capa 2' "GET $Identity/api/_meta/entorno responde produccion" $true {
  $meta = Invoke-RestMethod "$Identity/api/_meta/entorno" -TimeoutSec 10
  Exigir ($meta.entorno -eq 'produccion') "El servicio que responde es de '$($meta.entorno)'"
  "servicio=$($meta.servicio), entorno=$($meta.entorno), version=$($meta.version)"
}

$s = if (Test-Path $Secretos) { Get-Content $Secretos -Raw | ConvertFrom-Json } else { $null }
$tokenAdmin = $null

Paso 'Capa 2' 'El login de producción emite un token con el emisor de producción' $true {
  Exigir ($null -ne $s) "No están los secretos de producción ($Secretos). Ejecute scripts\levantar-produccion.ps1."
  $sesion = Login $Identity $s.adminEmail $s.adminPassword
  $script:tokenAdmin = $sesion.accessToken
  $p = Payload $sesion.accessToken
  Exigir ($p.iss -eq $EmisorProduccion) "iss = '$($p.iss)'"
  "iss=$($p.iss)"
}

Paso 'Capa 2' 'El dato conocido de producción existe (organización de verificación)' $true {
  Exigir ($null -ne $script:tokenAdmin) 'Sin token de producción.'
  $org = Invoke-RestMethod "$Identity/api/tenants/$TenantVerificacion" -Headers @{ Authorization = "Bearer $script:tokenAdmin" } -TimeoutSec 10
  Exigir ($org.tenantId -eq $TenantVerificacion) 'No apareció la organización de verificación.'
  "$($org.nombre) ($($org.tenantId))"
}

Paso 'Capa 2' 'El conductor de verificación inicia sesión en producción' $true {
  Exigir ($null -ne $s) 'Sin secretos de producción.'
  $sesion = Login $Identity $s.conductorEmail $s.conductorPassword
  $p = Payload $sesion.accessToken
  Exigir ($p.tenant_id -eq $TenantVerificacion -and $p.role -eq 'Conductor') "tenant_id=$($p.tenant_id), role=$($p.role)"
  "role=$($p.role), tenant_id=$($p.tenant_id)"
}

Paso 'Capa 2' 'Un token de desarrollo no sirve en producción' $false {
  try { $dev = Login $IdentityDesarrollo 'admin@ecotrace.local' 'EcoTrace-Admin-Dev-2026' }
  catch { return "Omitido: desarrollo no responde en $IdentityDesarrollo" }
  try {
    Invoke-RestMethod "$Identity/api/auth/sesion" -Headers @{ Authorization = "Bearer $($dev.accessToken)" } -TimeoutSec 10 | Out-Null
    throw 'Producción aceptó un token de desarrollo.'
  }
  catch [System.Net.WebException] {
    $codigo = [int] $_.Exception.Response.StatusCode
    Exigir ($codigo -eq 401) "Respondió $codigo"
    '401 (otro emisor y otra clave)'
  }
}

# ---------------------------------------------------------------- Resultado

$resultados | Format-Table Capa, Paso, Bloquea, Resultado, Detalle -AutoSize -Wrap | Out-String -Width 200 | Write-Host
$bloqueantes = @($resultados | Where-Object { $_.Bloquea -and $_.Resultado -ne 'Cumple' })
if ($bloqueantes.Count -gt 0) {
  Write-Host "RELEASE BLOQUEADO: $($bloqueantes.Count) paso(s) bloqueante(s) fallaron." -ForegroundColor Red
  exit 1
}

Write-Host "Release aprobado: $($resultados.Count) pasos, ninguno bloqueante falló." -ForegroundColor Green
exit 0
