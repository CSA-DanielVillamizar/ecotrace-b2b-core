#!/usr/bin/env pwsh
<#
.SYNOPSIS
  Verificación de release en dos capas (ADR 0006). Termina con código 1 si falla un paso que bloquea el release.

.DESCRIPTION
  Capa 1, configuración: lo que se puede comprobar sin ejecutar nada (la app de Release lleva la configuración de
  producción, el emisor de producción no es el de desarrollo, no hay claves en el repositorio, el build no es de Debug).

  Capa 2, comportamiento real: pregunta a los servicios que están corriendo. Un servicio dice en qué entorno está
  (/api/_meta/entorno), el registro conocido de producción existe con su identificador fijo, y un token con el emisor de
  desarrollo no sirve. Si la app apuntara por error a desarrollo, la pantalla funcionaría igual, pero estas comprobaciones no.

  Con -LevantarProduccion el script arranca por su cuenta la producción simulada (puertos 6101-6104, otra clave de firma,
  secretos aleatorios, bases temporales) y la detiene al terminar. Sin él, espera encontrarla ya corriendo.

.EXAMPLE
  pwsh scripts/verificar-release.ps1 -LevantarProduccion
  pwsh scripts/verificar-release.ps1 -LevantarProduccion -Apk bin/Release/net8.0-android/app-Signed.apk
#>
[CmdletBinding()]
param(
    [switch]$LevantarProduccion,
    # Deja la producción simulada corriendo al terminar, para probar la app de Release contra ella (se detiene con Stop-Process).
    [switch]$DejarCorriendo,
    # Contraseña del administrador de la producción simulada. Si no se da, es aleatoria y nadie la conoce.
    [string]$ClaveDeAdministrador,
    # Si el APK de Release existe, se comprueba su firma y que no sea depurable.
    [string]$Apk,
    # Si hay un entorno de desarrollo corriendo, se comprueba además que no comparte nada con producción.
    [string]$IdentityDesarrollo = 'http://localhost:5101',
    [string]$AdminDesarrolloEmail = 'admin@ecotrace.local',
    [string]$AdminDesarrolloClave = 'Plataforma-2026-dev'
)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.Encoding]::UTF8
$raiz = Split-Path -Parent $PSScriptRoot
$emisorProduccion = 'ecotrace-identity-produccion'
$emisorDesarrollo = 'ecotrace-identity-desarrollo'
$registroConocido = '6f1c0a52-3b7e-4d8a-9c41-0e5a7b2d9f10'
$nombreRegistroConocido = 'Organización de verificación (producción)'
$servicios = [ordered]@{
    identity = @{ Puerto = 6101; Proyecto = 'src/EcoTrace.Identity/Api' }
    fleet    = @{ Puerto = 6102; Proyecto = 'src/EcoTrace.FleetManagement/Api' }
    cargo    = @{ Puerto = 6103; Proyecto = 'src/EcoTrace.CargoTracking/Api' }
    billing  = @{ Puerto = 6104; Proyecto = 'src/EcoTrace.Billing/Api' }
}

$resultados = [System.Collections.Generic.List[object]]::new()
function Registrar([string]$capa, [string]$paso, [string]$estado, [string]$detalle, [bool]$bloquea = $true) {
    $resultados.Add([pscustomobject]@{ Capa = $capa; Paso = $paso; Estado = $estado; Bloquea = $bloquea; Detalle = $detalle })
    $color = switch ($estado) { 'OK' { 'Green' } 'FALLA' { 'Red' } default { 'Yellow' } }
    Write-Host ("[{0,-6}] {1} {2}: {3}" -f $estado, $capa, $paso, $detalle) -ForegroundColor $color
}
function Comprobar([string]$capa, [string]$paso, [scriptblock]$prueba, [bool]$bloquea = $true) {
    try {
        $detalle = & $prueba
        Registrar $capa $paso 'OK' ([string]$detalle) $bloquea
    }
    catch {
        Registrar $capa $paso 'FALLA' $_.Exception.Message $bloquea
    }
}

function Base64Url([byte[]]$bytes) { [Convert]::ToBase64String($bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_') }

# Un token con la forma correcta, firmado con una clave que nadie más conoce y con el emisor que se indique.
function Nuevo-TokenForjado([string]$emisor, [string]$audiencia) {
    $rsa = [System.Security.Cryptography.RSA]::Create(2048)
    $encabezado = Base64Url ([Text.Encoding]::UTF8.GetBytes('{"alg":"RS256","typ":"JWT","kid":"forjado"}'))
    $ahora = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
    $cuerpo = @{ iss = $emisor; aud = $audiencia; sub = [guid]::NewGuid().ToString(); role = 'Administrador'; plataforma = 'true'; iat = $ahora; exp = $ahora + 300 } | ConvertTo-Json -Compress
    $firmando = "$encabezado." + (Base64Url ([Text.Encoding]::UTF8.GetBytes($cuerpo)))
    $firma = $rsa.SignData([Text.Encoding]::ASCII.GetBytes($firmando), [System.Security.Cryptography.HashAlgorithmName]::SHA256, [System.Security.Cryptography.RSASignaturePadding]::Pkcs1)
    "$firmando." + (Base64Url $firma)
}

function Llamar([string]$url, [hashtable]$encabezados = @{}, [string]$metodo = 'GET', $cuerpo = $null) {
    $parametros = @{ Uri = $url; Method = $metodo; Headers = $encabezados; SkipHttpErrorCheck = $true; TimeoutSec = 15 }
    if ($null -ne $cuerpo) { $parametros.Body = ($cuerpo | ConvertTo-Json -Compress); $parametros.ContentType = 'application/json' }
    $respuesta = Invoke-WebRequest @parametros
    $json = $null
    if ($respuesta.Content) { try { $json = $respuesta.Content | ConvertFrom-Json } catch { } }
    [pscustomobject]@{ Estado = [int]$respuesta.StatusCode; Json = $json }
}

# --------------------------------------------------------------------------------------------------------------------
# Capa 1: configuración
# --------------------------------------------------------------------------------------------------------------------
Write-Host "`n== Capa 1: configuración ==" -ForegroundColor Cyan

$produccionApp = Get-Content (Join-Path $raiz 'src/EcoTrace.App/Configuracion/appsettings.Produccion.json') -Raw | ConvertFrom-Json
$desarrolloApp = Get-Content (Join-Path $raiz 'src/EcoTrace.App/Configuracion/appsettings.Desarrollo.json') -Raw | ConvertFrom-Json

Comprobar 'C1' 'El build Release de la app incrusta la configuración de producción' {
    $proyecto = Get-Content (Join-Path $raiz 'src/EcoTrace.App/EcoTrace.App.csproj') -Raw
    if ($proyecto -notmatch "(?s)Condition=""'\`$\(Configuration\)' == 'Release'"">\s*<EmbeddedResource Include=""Configuracion\\appsettings\.Produccion\.json""") {
        throw 'El csproj no elige appsettings.Produccion.json para la configuración Release.'
    }
    if ($produccionApp.entorno -ne 'produccion') { throw "appsettings.Produccion.json declara entorno '$($produccionApp.entorno)'." }
    'Release -> appsettings.Produccion.json (entorno produccion)'
}

Comprobar 'C1' 'Las URLs de producción de la app no son las de desarrollo' {
    foreach ($nombre in 'identity', 'fleet', 'cargo', 'billing') {
        $prod = $produccionApp.servicios.$nombre
        if ($prod -eq $desarrolloApp.servicios.$nombre) { throw "$nombre apunta a la misma URL en producción y en desarrollo ($prod)." }
        if ($prod -match ':510[1-4]\b') { throw "$nombre usa un puerto de desarrollo ($prod)." }
    }
    ($produccionApp.servicios.PSObject.Properties | ForEach-Object { "$($_.Name)=$($_.Value)" }) -join ' '
}

Comprobar 'C1' 'La app no tiene direcciones escritas en el código' {
    $encontradas = Get-ChildItem (Join-Path $raiz 'src/EcoTrace.App') -Recurse -Include *.cs, *.xaml |
        Where-Object { $_.FullName -notmatch '[\\/](obj|bin|Configuracion)[\\/]' } |
        Select-String -Pattern 'https?://(?!schemas\.|www\.w3\.org|xamarin\.com|docs\.microsoft)' |
        Where-Object { $_.Line -notmatch '^\s*(///|//)' -and $_.Line -notmatch 'xmlns' }
    if ($encontradas) { throw ("Hay direcciones en el código: " + (($encontradas | Select-Object -First 3 | ForEach-Object { "$($_.Filename):$($_.LineNumber)" }) -join ', ')) }
    'ninguna fuera de Configuracion/'
}

Comprobar 'C1' 'El emisor de tokens de producción es distinto al de desarrollo, en los cuatro servicios' {
    $proyectos = 'EcoTrace.Identity', 'EcoTrace.FleetManagement', 'EcoTrace.CargoTracking', 'EcoTrace.Billing'
    foreach ($p in $proyectos) {
        $dev = (Get-Content (Join-Path $raiz "src/$p/Api/appsettings.json") -Raw | ConvertFrom-Json).Jwt.Issuer
        $prod = (Get-Content (Join-Path $raiz "src/$p/Api/appsettings.Production.json") -Raw | ConvertFrom-Json).Jwt.Issuer
        if ($prod -ne $emisorProduccion) { throw "$p declara el emisor '$prod' en producción." }
        if ($dev -ne $emisorDesarrollo) { throw "$p declara el emisor '$dev' en desarrollo." }
    }
    "$emisorProduccion != $emisorDesarrollo"
}

Comprobar 'C1' 'No hay claves ni secretos en los archivos de producción del repositorio' {
    $archivos = Get-ChildItem (Join-Path $raiz 'src') -Recurse -Filter 'appsettings.Production.json' | Where-Object { $_.FullName -notmatch '[\\/](obj|bin)[\\/]' }
    $archivos += Get-ChildItem (Join-Path $raiz 'src/EcoTrace.App/Configuracion') -Filter *.json
    foreach ($a in $archivos) {
        $texto = Get-Content $a.FullName -Raw
        if ($texto -match 'PRIVATE KEY|PrivateKeyPem|ClientSecret|AdminPassword') { throw "$($a.Name) contiene una clave o un secreto." }
    }
    "$($archivos.Count) archivos revisados"
}

Comprobar 'C1' 'Compilado en Release, el Mobile.Core no es de Debug' {
    dotnet build (Join-Path $raiz 'src/EcoTrace.Mobile.Core/EcoTrace.Mobile.Core.csproj') -c Release --nologo -v q | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'No compiló en Release.' }
    $dll = Join-Path $raiz 'src/EcoTrace.Mobile.Core/bin/Release/net8.0/EcoTrace.Mobile.Core.dll'
    $atributo = [System.Reflection.Assembly]::LoadFile((Resolve-Path $dll)).GetCustomAttributes($false) |
        Where-Object { $_.GetType().Name -eq 'DebuggableAttribute' }
    if ($atributo -and $atributo.IsJITOptimizerDisabled) { throw 'El ensamblado tiene las optimizaciones desactivadas (Debug).' }
    'optimizaciones activas'
}

# El APK solo se revisa si se entrega: no se afirma nada sobre un build que no existe.
if ($Apk) {
    $sdk = if ($env:ANDROID_HOME) { $env:ANDROID_HOME } elseif ($env:ANDROID_SDK_ROOT) { $env:ANDROID_SDK_ROOT } else { Join-Path $env:LOCALAPPDATA 'Android/Sdk' }
    $herramientas = Get-ChildItem (Join-Path $sdk 'build-tools') -Directory -ErrorAction SilentlyContinue | Sort-Object Name -Descending | Select-Object -First 1

    Comprobar 'C1' 'El APK de Release está firmado con una clave que no es la de Debug' {
        if (-not (Test-Path $Apk)) { throw "No existe $Apk." }
        $extension = if ($IsWindows) { '.bat' } else { '' }
        $firmante = & (Join-Path $herramientas.FullName "apksigner$extension") verify --print-certs $Apk 2>&1 | Out-String
        $huella = [regex]::Match($firmante, 'SHA-256 digest: ([0-9a-f]{64})').Groups[1].Value
        if (-not $huella) { throw "apksigner no verificó el APK:`n$firmante" }
        # Dos pruebas: el certificado no es el que genera Android/MAUI para Debug («CN=Android Debug»), y su huella no coincide con la
        # de ningún keystore de Debug de esta máquina (el de Android Studio o el de Xamarin/MAUI).
        $sujeto = [regex]::Match($firmante, 'certificate DN: (.+)').Groups[1].Value.Trim()
        if ($sujeto -match 'Android Debug') { throw "El APK está firmado con un certificado de Debug ($sujeto)." }

        $candidatos = @((Join-Path $HOME '.android/debug.keystore'))
        if ($env:LOCALAPPDATA) { $candidatos += Join-Path $env:LOCALAPPDATA 'Xamarin/Mono for Android/debug.keystore' }
        $comparados = 0
        foreach ($depuracion in $candidatos | Where-Object { Test-Path $_ }) {
            $salida = keytool -list -v -keystore $depuracion -storepass android -alias androiddebugkey 2>&1 | Out-String
            $huellaDebug = ([regex]::Match($salida, 'SHA256: ([0-9A-F:]{95})').Groups[1].Value -replace ':', '').ToLower()
            if (-not $huellaDebug) { continue }
            $comparados++
            if ($huellaDebug -eq $huella) { throw "El APK está firmado con el keystore de Debug ($depuracion)." }
        }

        "SHA-256 del firmante: $huella ($sujeto); comparado con $comparados keystore(s) de Debug"
    }

    Comprobar 'C1' 'El APK de Release no es depurable' {
        $aapt = Join-Path $herramientas.FullName ('aapt' + $(if ($IsWindows) { '.exe' } else { '' }))
        $datos = & $aapt dump badging $Apk | Out-String
        if ($datos -match 'application-debuggable') { throw 'El APK declara android:debuggable.' }
        'sin android:debuggable'
    }
}
else {
    Registrar 'C1' 'Firma y depuración del APK de Release' 'OMITIDO' 'no se entregó -Apk: no se afirma nada sobre un build que no se ha visto' $false
}

# --------------------------------------------------------------------------------------------------------------------
# Producción simulada (solo con -LevantarProduccion)
# --------------------------------------------------------------------------------------------------------------------
$procesos = @()
$carpetaTemporal = $null
$claveAdmin = $null
$correoAdmin = 'verificacion@ecotrace.prod'
if ($LevantarProduccion) {
    Write-Host "`nLevantando la producción simulada (puertos 6101-6104, otra clave de firma)..." -ForegroundColor Cyan
    dotnet build (Join-Path $raiz 'EcoTrace.sln') -c Release --nologo -v q | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'La solución no compiló en Release.' }

    $carpetaTemporal = Join-Path ([IO.Path]::GetTempPath()) ("ecotrace-prod-" + [guid]::NewGuid().ToString('N').Substring(0, 8))
    New-Item -ItemType Directory -Path $carpetaTemporal | Out-Null
    $rsa = [System.Security.Cryptography.RSA]::Create(2048)
    $pem = $rsa.ExportPkcs8PrivateKeyPem()
    $claveAdmin = if ($ClaveDeAdministrador) { $ClaveDeAdministrador } else { 'Prod-' + [guid]::NewGuid().ToString('N') }
    $secretoBilling = [guid]::NewGuid().ToString('N') + [guid]::NewGuid().ToString('N')
    $secretoCargo = [guid]::NewGuid().ToString('N') + [guid]::NewGuid().ToString('N')

    foreach ($nombre in $servicios.Keys) {
        $s = $servicios[$nombre]
        $info = [Diagnostics.ProcessStartInfo]::new('dotnet')
        foreach ($a in 'run', '--no-launch-profile', '-c', 'Release', '--project', (Join-Path $raiz $s.Proyecto), '--urls', "http://localhost:$($s.Puerto)") { $info.ArgumentList.Add($a) }
        $info.UseShellExecute = $false
        $info.RedirectStandardOutput = $true
        $info.RedirectStandardError = $true
        $info.Environment['ASPNETCORE_ENVIRONMENT'] = 'Production'
        $info.Environment['ConnectionStrings__Default'] = "Data Source=$(Join-Path $carpetaTemporal "$nombre.db")"
        $info.Environment['Swagger__Habilitado'] = 'false'
        $info.Environment['Jwt__PrivateKeyPem'] = $pem
        $info.Environment['Plataforma__AdminEmail'] = $correoAdmin
        $info.Environment['Plataforma__AdminPassword'] = $claveAdmin
        $info.Environment['Servicios__Clientes__0__ClientId'] = 'billing'
        $info.Environment['Servicios__Clientes__0__ClientSecret'] = $secretoBilling
        $info.Environment['Servicios__Clientes__0__Audiencias__0'] = 'ecotrace-identity'
        $info.Environment['Servicios__Clientes__0__Audiencias__1'] = 'ecotrace-fleet'
        $info.Environment['Servicios__Clientes__1__ClientId'] = 'cargo'
        $info.Environment['Servicios__Clientes__1__ClientSecret'] = $secretoCargo
        $info.Environment['Servicios__Clientes__1__Audiencias__0'] = 'ecotrace-billing'
        $info.Environment['Servicio__ClientId'] = if ($nombre -eq 'billing') { 'billing' } else { 'cargo' }
        $info.Environment['Servicio__ClientSecret'] = if ($nombre -eq 'billing') { $secretoBilling } else { $secretoCargo }
        $info.Environment['Servicios__Identity__BaseUrl'] = "http://localhost:$($servicios.identity.Puerto)"
        $info.Environment['Servicios__Fleet__BaseUrl'] = "http://localhost:$($servicios.fleet.Puerto)"
        $info.Environment['Servicios__Billing__BaseUrl'] = "http://localhost:$($servicios.billing.Puerto)"
        $procesos += [Diagnostics.Process]::Start($info)
    }
}

function Esperar-Servicio([int]$puerto) {
    # Si el script acaba de arrancar los servicios, tardan en compilar y migrar; si no, o están o no están.
    $intentos = if ($LevantarProduccion) { 90 } else { 3 }
    for ($i = 0; $i -lt $intentos; $i++) {
        try { if ((Invoke-WebRequest "http://localhost:$puerto/health" -TimeoutSec 2 -SkipHttpErrorCheck).StatusCode -eq 200) { return $true } } catch { }
        Start-Sleep -Seconds 1
    }
    $false
}

try {
    # ----------------------------------------------------------------------------------------------------------------
    # Capa 2: comportamiento real
    # ----------------------------------------------------------------------------------------------------------------
    Write-Host "`n== Capa 2: comportamiento real contra la producción ==" -ForegroundColor Cyan

    $arriba = $true
    foreach ($nombre in $servicios.Keys) {
        if (-not (Esperar-Servicio $servicios[$nombre].Puerto)) {
            Registrar 'C2' "El servicio $nombre responde en $($servicios[$nombre].Puerto)" 'FALLA' 'no respondió /health a tiempo' $true
            $arriba = $false
        }
    }

    if ($arriba) {
        foreach ($nombre in $servicios.Keys) {
            Comprobar 'C2' "El servicio $nombre dice que es producción" {
                $r = Llamar "http://localhost:$($servicios[$nombre].Puerto)/api/_meta/entorno"
                if ($r.Estado -ne 200) { throw "GET /api/_meta/entorno respondió $($r.Estado)." }
                if ($r.Json.entorno -ne 'produccion') { throw "dice entorno '$($r.Json.entorno)'." }
                if ($r.Json.emisor -ne $emisorProduccion) { throw "reconoce el emisor '$($r.Json.emisor)'." }
                "entorno=$($r.Json.entorno) version=$($r.Json.version) emisor=$($r.Json.emisor)"
            }
        }

        $identity = "http://localhost:$($servicios.identity.Puerto)"
        $tokenProduccion = $null
        if ($claveAdmin) {
            Comprobar 'C2' 'El administrador de producción inicia sesión con un token de producción' {
                $r = Llamar "$identity/api/auth/login" @{} 'POST' @{ email = $correoAdmin; password = $claveAdmin }
                if ($r.Estado -ne 200) { throw "login respondió $($r.Estado)." }
                $script:tokenProduccion = $r.Json.accessToken
                $cargaUtil = $script:tokenProduccion.Split('.')[1].Replace('-', '+').Replace('_', '/')
                $cargaUtil = $cargaUtil.PadRight($cargaUtil.Length + (4 - $cargaUtil.Length % 4) % 4, '=')
                $iss = ([Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($cargaUtil)) | ConvertFrom-Json).iss
                if ($iss -ne $emisorProduccion) { throw "el token trae el emisor '$iss'." }
                "iss=$iss"
            }
        }
        else {
            Registrar 'C2' 'Token de producción' 'OMITIDO' 'sin -LevantarProduccion no se conoce la contraseña del administrador de producción' $false
        }

        if ($tokenProduccion) {
            $auth = @{ Authorization = "Bearer $tokenProduccion" }

            Comprobar 'C2' 'El registro conocido de producción existe con su identificador fijo' {
                $r = Llamar "$identity/api/tenants/$registroConocido" $auth
                if ($r.Estado -ne 200) { throw "GET /api/tenants/$registroConocido respondió $($r.Estado)." }
                if ($r.Json.nombre -ne $nombreRegistroConocido) { throw "el nombre es '$($r.Json.nombre)'." }
                "$registroConocido -> $($r.Json.nombre)"
            }

            Comprobar 'C2' 'Un token de producción lo aceptan Fleet, Cargo y Billing' {
                foreach ($par in @(@('fleet', 'vehiculos'), @('cargo', 'cargas'), @('billing', 'pagos'))) {
                    $r = Llamar "http://localhost:$($servicios[$par[0]].Puerto)/api/$($par[1])" $auth
                    if ($r.Estado -ne 200) { throw "$($par[0]) respondió $($r.Estado) a GET /api/$($par[1])." }
                }
                'los tres responden 200'
            }
        }

        Comprobar 'C2' 'Un token con el emisor de desarrollo no sirve en producción' {
            $forjado = Nuevo-TokenForjado $emisorDesarrollo 'ecotrace-fleet'
            $r = Llamar "http://localhost:$($servicios.fleet.Puerto)/api/vehiculos" @{ Authorization = "Bearer $forjado" }
            if ($r.Estado -ne 401) { throw "Fleet de producción respondió $($r.Estado) en vez de 401." }
            '401'
        }

        # Si hay un desarrollo corriendo, se comprueba con un token real de desarrollo y con su ausencia del registro conocido.
        $desarrolloArriba = $false
        try { $desarrolloArriba = (Invoke-WebRequest "$IdentityDesarrollo/health" -TimeoutSec 2 -SkipHttpErrorCheck).StatusCode -eq 200 } catch { }
        if ($desarrolloArriba) {
            Comprobar 'C2' 'Un token real de desarrollo no sirve en producción, y el registro conocido no existe en desarrollo' {
                $login = Llamar "$IdentityDesarrollo/api/auth/login" @{} 'POST' @{ email = $AdminDesarrolloEmail; password = $AdminDesarrolloClave }
                if ($login.Estado -ne 200) { throw "no se pudo iniciar sesión en desarrollo ($($login.Estado))." }
                $deDesarrollo = @{ Authorization = "Bearer $($login.Json.accessToken)" }
                $r = Llamar "http://localhost:$($servicios.fleet.Puerto)/api/vehiculos" $deDesarrollo
                if ($r.Estado -ne 401) { throw "Fleet de producción aceptó (o rechazó con $($r.Estado)) un token de desarrollo." }
                $r = Llamar "$IdentityDesarrollo/api/tenants/$registroConocido" $deDesarrollo
                if ($r.Estado -ne 404) { throw "el registro conocido de producción respondió $($r.Estado) en desarrollo: los entornos se mezclan." }
                '401 en producción y 404 en desarrollo'
            }
        }
        else {
            Registrar 'C2' 'Cruce con un desarrollo real' 'OMITIDO' "no hay un Identity de desarrollo en $IdentityDesarrollo; queda cubierto el token forjado" $false
        }
    }
}
finally {
    if ($DejarCorriendo -and $procesos) {
        Write-Host "La producción simulada sigue corriendo (puertos 6101-6104, bases en $carpetaTemporal). PIDs: $(($procesos | ForEach-Object Id) -join ', ')" -ForegroundColor Yellow
    }
    else {
        foreach ($p in $procesos) { if (-not $p.HasExited) { $p.Kill($true) } }
    }
    if (-not $DejarCorriendo -and $carpetaTemporal -and (Test-Path $carpetaTemporal)) { Start-Sleep -Seconds 1; Remove-Item $carpetaTemporal -Recurse -Force -ErrorAction SilentlyContinue }
}

# --------------------------------------------------------------------------------------------------------------------
$fallas = @($resultados | Where-Object { $_.Estado -eq 'FALLA' -and $_.Bloquea })
$omitidos = @($resultados | Where-Object { $_.Estado -eq 'OMITIDO' })
Write-Host ''
Write-Host ("Resultado: {0} correctos, {1} fallas que bloquean, {2} omitidos." -f @($resultados | Where-Object Estado -eq 'OK').Count, $fallas.Count, $omitidos.Count) -ForegroundColor $(if ($fallas) { 'Red' } else { 'Green' })
if ($omitidos) { Write-Host 'Lo omitido NO está verificado:' -ForegroundColor Yellow; $omitidos | ForEach-Object { Write-Host "  - $($_.Paso): $($_.Detalle)" -ForegroundColor Yellow } }
if ($fallas) { Write-Host 'RELEASE BLOQUEADO.' -ForegroundColor Red; exit 1 }
Write-Host 'Release aprobado en lo verificado.' -ForegroundColor Green
exit 0
