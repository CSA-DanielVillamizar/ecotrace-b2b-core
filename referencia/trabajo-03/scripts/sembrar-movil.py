#!/usr/bin/env python3
"""
Siembra datos para probar la app del conductor contra la plataforma que corre en local (scripts/run-all).

Crea, por la API y con el token de la persona que corresponde en cada paso (como lo exigen los servicios):
  - dos organizaciones (un generador y un transportista),
  - un administrador del generador, un supervisor y un conductor del transportista (con contraseña),
  - un vehículo y el conductor de Fleet vinculado a la cuenta del conductor,
  - dos cargas asignadas a ese conductor (una en tránsito, lista para entregar) y el pago en custodia de la primera.

Al final imprime con qué cuenta entrar en la app. Es un script de desarrollo: usa las credenciales de desarrollo.
"""
import json
import os
import random
import string
import sys
import urllib.error
import urllib.request

BASES = {
    "identity": os.environ.get("ECOTRACE_IDENTITY", "http://localhost:5101"),
    "fleet": os.environ.get("ECOTRACE_FLEET", "http://localhost:5102"),
    "cargo": os.environ.get("ECOTRACE_CARGO", "http://localhost:5103"),
    "billing": os.environ.get("ECOTRACE_BILLING", "http://localhost:5104"),
}
ADMIN = (os.environ.get("ECOTRACE_ADMIN_CORREO", "admin@ecotrace.local"), os.environ.get("ECOTRACE_ADMIN_CLAVE", "Plataforma-2026-dev"))
CLAVE = "Conductor-2026-demo"


def llamar(servicio, metodo, ruta, cuerpo=None, token=None):
    solicitud = urllib.request.Request(
        BASES[servicio] + ruta, method=metodo,
        data=None if cuerpo is None else json.dumps(cuerpo).encode(),
        headers={"Content-Type": "application/json", **({"Authorization": f"Bearer {token}"} if token else {})})
    try:
        with urllib.request.urlopen(solicitud, timeout=15) as r:
            texto = r.read().decode()
            return json.loads(texto) if texto else None
    except urllib.error.HTTPError as e:
        sys.exit(f"{metodo} {servicio}{ruta} -> {e.code}: {e.read().decode()[:300]}")
    except urllib.error.URLError as e:
        sys.exit(f"No se pudo conectar con {servicio} ({BASES[servicio]}): {e.reason}. ¿Está corriendo scripts/run-all?")


def entrar(email, clave):
    return llamar("identity", "POST", "/api/auth/login", {"email": email, "password": clave})["accessToken"]


sufijo = "".join(random.choices(string.ascii_lowercase + string.digits, k=5))
operador = entrar(*ADMIN)

generador = llamar("identity", "POST", "/api/tenants", {"nombre": f"Generadora Demo {sufijo}", "tenantType": "Generador"}, operador)
transportista = llamar("identity", "POST", "/api/tenants", {"nombre": f"Transportes Demo {sufijo}", "tenantType": "Transportista"}, operador)


def usuario(tenant, rol, nombre, correo):
    return llamar("identity", "POST", "/api/users",
                  {"tenantId": tenant["tenantId"], "role": rol, "nombre": nombre, "email": correo, "password": CLAVE}, operador)


admin_gen = usuario(generador, "Administrador", "Laura Mejía", f"laura.{sufijo}@generadora.demo")
supervisor = usuario(transportista, "Supervisor", "Marta Ríos", f"marta.{sufijo}@transportes.demo")
conductor = usuario(transportista, "Conductor", "Carlos Ruiz", f"carlos.{sufijo}@transportes.demo")

t_admin_gen = entrar(admin_gen["email"], CLAVE)
t_supervisor = entrar(supervisor["email"], CLAVE)

vehiculo = llamar("fleet", "POST", "/api/vehiculos", {
    "tenantId": transportista["tenantId"], "registradoPorUserId": supervisor["userId"],
    "placa": "D" + "".join(random.choices(string.ascii_uppercase + string.digits, k=5)), "capacidadKg": 8000}, t_supervisor)
conductor_fleet = llamar("fleet", "POST", "/api/conductores", {
    "tenantId": transportista["tenantId"], "registradoPorUserId": supervisor["userId"], "userId": conductor["userId"],
    "nombre": "Carlos Ruiz", "licencia": "C2-" + "".join(random.choices(string.digits, k=6))}, t_supervisor)

cargas = []
for descripcion, origen, destino, peso in (
        ("Residuos industriales no peligrosos", "Cali", "Popayán", 4200),
        ("Lodos de tratamiento de aguas", "Cali", "Palmira", 3100)):
    carga = llamar("cargo", "POST", "/api/cargas", {
        "generadorTenantId": generador["tenantId"], "transportistaTenantId": transportista["tenantId"],
        "descripcion": descripcion, "origen": origen, "destino": destino, "pesoKg": peso}, t_admin_gen)
    llamar("cargo", "POST", f"/api/cargas/{carga['cargaId']}/asignacion",
           {"vehiculoId": vehiculo["vehiculoId"], "conductorId": conductor_fleet["conductorId"]}, t_supervisor)
    cargas.append(carga)

llamar("cargo", "POST", f"/api/cargas/{cargas[0]['cargaId']}/seguimientos",
       {"estado": "EnTransito", "ubicacion": "Santander de Quilichao"}, t_supervisor)
pago = llamar("billing", "POST", "/api/pagos", {
    "generadorTenantId": generador["tenantId"], "transportistaTenantId": transportista["tenantId"],
    "cargaId": cargas[0]["cargaId"], "monto": 850000}, t_admin_gen)

print("Datos listos. Entra en la app con:")
print(f"  Conductor:  {conductor['email']}  /  {CLAVE}")
print(f"  Supervisor: {supervisor['email']}  /  {CLAVE}")
print(f"  Admin del generador: {admin_gen['email']}  /  {CLAVE}")
print(f"  Carga lista para entregar: {cargas[0]['descripcion']} (id {cargas[0]['cargaId']})")
print(f"  Pago en custodia: {pago['pago']['pagoId']}")
