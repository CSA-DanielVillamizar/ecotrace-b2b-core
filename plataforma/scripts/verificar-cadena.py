"""
Recorre la cadena completa del Trabajo 3 contra los servicios reales (no en memoria) y dice si cada paso cumple.

  Identity (login, organizaciones, usuarios) -> Fleet (vehiculo, conductor, reserva con token de servicio)
  -> Cargo (carga, asignacion, entrega con Idempotency-Key, reintento sin duplicar, 401 sin token)
  -> Outbox -> Billing -> Saga (Identity autoriza, Fleet libera) -> pago Liberado

Uso (con la plataforma levantada en desarrollo: scripts/run-all.ps1):
  python scripts/verificar-cadena.py
Termina con codigo 1 en el primer paso que falla.
"""
import json
import sys
import time
import urllib.error
import urllib.request
import uuid

I, F, C, B = "http://localhost:5101", "http://localhost:5102", "http://localhost:5103", "http://localhost:5104"
ADMIN = ("admin@ecotrace.local", "EcoTrace-Admin-Dev-2026")
SECRETO_BILLING = "billing-dev-secreto-solo-local"
CLAVE = "Clave-2026-demo"


def req(metodo, url, cuerpo=None, token=None, clave=None):
    datos = None if cuerpo is None else json.dumps(cuerpo).encode()
    solicitud = urllib.request.Request(url, data=datos, method=metodo)
    solicitud.add_header("Content-Type", "application/json")
    if token:
        solicitud.add_header("Authorization", "Bearer " + token)
    if clave:
        solicitud.add_header("Idempotency-Key", clave)
    try:
        with urllib.request.urlopen(solicitud, timeout=20) as respuesta:
            texto = respuesta.read().decode()
            return respuesta.status, (json.loads(texto) if texto else None)
    except urllib.error.HTTPError as error:
        texto = error.read().decode()
        return error.code, (json.loads(texto) if texto.strip().startswith(("{", "[")) else texto)


def paso(nombre, estado, esperado, extra=""):
    esperados = esperado if isinstance(esperado, tuple) else (esperado,)
    ok = estado in esperados
    print(f"{'OK ' if ok else 'XX '} {nombre}: {estado} {extra}")
    if not ok:
        sys.exit(1)


def login(email, password):
    estado, cuerpo = req("POST", I + "/api/auth/login", {"email": email, "password": password})
    paso(f"login {email}", estado, 200)
    return cuerpo["accessToken"]


def servicio(audiencia):
    estado, cuerpo = req("POST", I + "/api/auth/token-servicio",
                         {"clientId": "billing", "clientSecret": SECRETO_BILLING, "audiencia": audiencia})
    paso(f"token de servicio billing -> {audiencia}", estado, 200)
    return cuerpo["accessToken"]


def main():
    sufijo = uuid.uuid4().hex[:6]
    admin = login(*ADMIN)

    estado, generador = req("POST", I + "/api/tenants", {"nombre": f"Generadora {sufijo}", "tenantType": "Generador"}, admin)
    paso("Identity crea el generador", estado, 201)
    estado, transportista = req("POST", I + "/api/tenants", {"nombre": f"Transportes {sufijo}", "tenantType": "Transportista"}, admin)
    paso("Identity crea el transportista", estado, 201)
    g, t = generador["tenantId"], transportista["tenantId"]

    personas = [("sup.gen", g, "Supervisor"), ("sup.tra", t, "Supervisor"), ("conductor", t, "Conductor")]
    for alias, organizacion, rol in personas:
        estado, _ = req("POST", I + "/api/users", {"tenantId": organizacion, "role": rol, "nombre": f"{rol} {alias}",
                                                   "email": f"{alias}.{sufijo}@x.co", "password": CLAVE}, admin)
        paso(f"Identity crea {alias} ({rol})", estado, 201)
    sup_gen, sup_tra, conductor = (login(f"{alias}.{sufijo}@x.co", CLAVE) for alias, _, _ in personas)

    estado, vehiculo = req("POST", F + "/api/vehiculos", {"placa": f"T{sufijo.upper()}", "capacidadKg": 5000}, sup_tra)
    paso("Fleet registra el vehiculo (token del supervisor)", estado, 201)
    estado, chofer = req("POST", F + "/api/conductores", {"nombre": "Carlos Ruiz", "licencia": f"C2-{sufijo}"}, sup_tra)
    paso("Fleet registra el conductor", estado, 201)

    estado, carga = req("POST", C + "/api/cargas", {"generadorTenantId": g, "transportistaTenantId": t,
                                                    "descripcion": "Residuos no peligrosos", "origen": "Cali",
                                                    "destino": "Popayan", "pesoKg": 4200}, sup_gen)
    paso("Cargo crea la carga (supervisor del generador)", estado, 201)
    carga_id = carga["cargaId"]
    recursos = {"vehiculoId": vehiculo["vehiculoId"], "conductorId": chofer["conductorId"]}
    estado, _ = req("POST", C + f"/api/cargas/{carga_id}/asignacion", recursos, sup_tra)
    paso("Cargo asigna la carga (supervisor del transportista)", estado, (200, 201))
    estado, _ = req("POST", F + "/api/reservas", {"cargaId": carga_id, **recursos}, servicio("ecotrace-fleet"))
    paso("Fleet reserva los recursos (token de servicio)", estado, (200, 201))
    estado, _ = req("POST", C + f"/api/cargas/{carga_id}/seguimientos", {"estado": "EnTransito", "ubicacion": "Santander"},
                    conductor, str(uuid.uuid4()))
    paso("Cargo: carga en transito (conductor)", estado, (200, 201))

    estado, pago = req("POST", B + "/api/pagos", {"generadorTenantId": g, "transportistaTenantId": t,
                                                  "cargaId": carga_id, "monto": 850000}, admin)
    paso("Billing deja el pago en custodia", estado, 201)
    pago_id = pago["pago"]["pagoId"]

    clave = str(uuid.uuid4())
    entrega = {"estado": "Entregado", "ubicacion": "Popayan"}
    estado, _ = req("POST", C + f"/api/cargas/{carga_id}/seguimientos", entrega, conductor, clave)
    paso("Cargo: ENTREGA del conductor con Idempotency-Key", estado, 201)
    estado, _ = req("POST", C + f"/api/cargas/{carga_id}/seguimientos", entrega, conductor, clave)
    paso("Cargo: reintento con la misma clave, sin duplicar", estado, 200)
    estado, _ = req("POST", C + f"/api/cargas/{carga_id}/seguimientos", entrega, None, str(uuid.uuid4()))
    paso("Cargo: sin token responde 401", estado, 401)

    print("... esperando Outbox -> Billing -> Saga (Identity autoriza, Fleet libera)")
    estado_pago = None
    for _ in range(60):
        time.sleep(2)
        estado, respuesta = req("GET", B + f"/api/pagos/{pago_id}", token=admin)
        estado_pago = respuesta["pago"]["estadoEscrow"] if estado == 200 else estado
        if estado_pago == "Liberado":
            break

    estado, sagas = req("GET", B + f"/api/sagas?pagoId={pago_id}", token=admin)
    saga = sagas[0] if isinstance(sagas, list) and sagas else {}
    print("   saga:", saga.get("estado"), [(p.get("paso") or p.get("nombre"), p.get("estado")) for p in saga.get("pasos", [])])
    paso("Billing: PAGO LIBERADO tras el Saga", 200 if estado_pago == "Liberado" else 500, 200, f"(estado={estado_pago})")

    estado, autorizacion = req("GET", I + f"/api/autorizaciones-pago/{pago_id}", token=servicio("ecotrace-identity"))
    paso("Identity registro la autorizacion del pago", estado, 200,
         autorizacion.get("estado") if isinstance(autorizacion, dict) else autorizacion)
    print(f"CADENA COMPLETA OK (carga {carga_id}, pago {pago_id})")


if __name__ == "__main__":
    main()
