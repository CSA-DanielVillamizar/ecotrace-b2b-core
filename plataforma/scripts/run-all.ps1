<#
.SYNOPSIS
  Compila la solucion y levanta los cuatro servicios y la consola web.

.DESCRIPTION
  Identity (5101), Fleet Management (5102), Cargo & Tracking (5103), Billing & Escrow (5104)
  y la consola (5100). Cada servicio crea su propia base de datos SQLite en su carpeta App_Data.
  Ctrl+C detiene todo.
#>
$ErrorActionPreference = 'Stop'
$raiz = Split-Path -Parent $PSScriptRoot
Set-Location $raiz

Write-Host 'Compilando la solucion...' -ForegroundColor Cyan
dotnet build EcoTrace.sln --nologo --verbosity quiet
if ($LASTEXITCODE -ne 0) { throw 'La compilacion fallo.' }

$env:ASPNETCORE_ENVIRONMENT = 'Development'

$servicios = @(
  @{ Nombre = 'Identity';        Proyecto = 'src/EcoTrace.Identity/Api';        Url = 'http://localhost:5101' },
  @{ Nombre = 'FleetManagement'; Proyecto = 'src/EcoTrace.FleetManagement/Api'; Url = 'http://localhost:5102' },
  @{ Nombre = 'CargoTracking';   Proyecto = 'src/EcoTrace.CargoTracking/Api';   Url = 'http://localhost:5103' },
  @{ Nombre = 'Billing';         Proyecto = 'src/EcoTrace.Billing/Api';         Url = 'http://localhost:5104' },
  @{ Nombre = 'Console';         Proyecto = 'src/EcoTrace.Console';             Url = 'http://localhost:5100' }
)

$procesos = foreach ($s in $servicios) {
  Start-Process dotnet -NoNewWindow -PassThru -ArgumentList @(
    'run', '--no-build', '--no-launch-profile', '--project', $s.Proyecto, '--urls', $s.Url)
}

Write-Host ''
Write-Host 'Consola:  http://localhost:5100' -ForegroundColor Green
Write-Host 'Swagger:  http://localhost:5101/swagger  (5102, 5103 y 5104 para los demas)'
Write-Host 'Ctrl+C detiene todos los servicios.'
Write-Host ''

try {
  Wait-Process -Id ($procesos | ForEach-Object Id)
}
finally {
  $procesos | Where-Object { -not $_.HasExited } | Stop-Process -Force -ErrorAction SilentlyContinue
}
