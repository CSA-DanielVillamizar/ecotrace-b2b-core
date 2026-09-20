#!/usr/bin/env bash
# Compila la solucion y levanta los cuatro servicios y la consola web.
#   Identity 5101 · Fleet Management 5102 · Cargo & Tracking 5103 · Billing & Escrow 5104 · Consola 5100
# Cada servicio crea su propia base de datos SQLite en su carpeta App_Data. Ctrl+C detiene todo.
set -euo pipefail

cd "$(dirname "$0")/.."

echo "Compilando la solucion..."
dotnet build EcoTrace.Referencia.sln --nologo --verbosity quiet

export ASPNETCORE_ENVIRONMENT=Development

pids=()
detener() {
  echo
  echo "Deteniendo servicios..."
  for pid in "${pids[@]}"; do kill "$pid" 2>/dev/null || true; done
}
trap detener EXIT INT TERM

iniciar() {
  dotnet run --no-build --no-launch-profile --project "$1" --urls "$2" &
  pids+=("$!")
}

iniciar src/EcoTrace.Identity/Api        http://localhost:5101
iniciar src/EcoTrace.FleetManagement/Api http://localhost:5102
iniciar src/EcoTrace.CargoTracking/Api   http://localhost:5103
iniciar src/EcoTrace.Billing/Api         http://localhost:5104
iniciar src/EcoTrace.Console             http://localhost:5100

echo
echo "Consola:  http://localhost:5100"
echo "Swagger:  http://localhost:5101/swagger  (5102, 5103 y 5104 para los demas)"
echo "Ctrl+C detiene todos los servicios."
echo

wait
