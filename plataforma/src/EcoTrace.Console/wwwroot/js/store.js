// Estado de la consola: copia en memoria de lo que devuelve cada servicio, mas la salud de cada uno.
// Las vistas se suscriben y se redibujan cuando llegan datos nuevos.

import { api, servicios } from './api.js';

export const estado = {
  tenants: [], users: [], roles: [],
  vehiculos: [], conductores: [],
  cargas: [],
  pagos: [], facturas: [],
  contextos: {},
  errores: {},
  salud: {},
  cargado: false,
  cargando: false
};

const oyentesDatos = new Set();
const oyentesSalud = new Set();

export function alCambiar(fn) {
  oyentesDatos.add(fn);
  return () => oyentesDatos.delete(fn);
}

export function alCambiarSalud(fn) {
  oyentesSalud.add(fn);
  return () => oyentesSalud.delete(fn);
}

const avisarDatos = () => oyentesDatos.forEach((fn) => fn());
const avisarSalud = () => oyentesSalud.forEach((fn) => fn());

/** Pide a la vista actual que se dibuje de nuevo, por ejemplo tras cambiar un filtro local. */
export const repintar = avisarDatos;

const LISTAS = {
  tenants: ['identity', () => api.identity.tenants()],
  users: ['identity', () => api.identity.users()],
  roles: ['identity', () => api.identity.roles()],
  vehiculos: ['fleet', () => api.fleet.vehiculos()],
  conductores: ['fleet', () => api.fleet.conductores()],
  cargas: ['cargo', () => api.cargo.cargas()],
  pagos: ['billing', () => api.billing.pagos()],
  facturas: ['billing', () => api.billing.facturas()]
};

/** Vuelve a pedir todas las listas. Si un servicio no responde, los demas siguen funcionando. */
export async function refrescar() {
  estado.cargando = true;
  avisarDatos();

  const claves = Object.keys(LISTAS);
  const resultados = await Promise.allSettled(claves.map((clave) => LISTAS[clave][1]()));

  estado.errores = {};
  claves.forEach((clave, i) => {
    const resultado = resultados[i];
    if (resultado.status === 'fulfilled') estado[clave] = resultado.value;
    else estado.errores[LISTAS[clave][0]] = resultado.reason;
  });

  estado.cargado = true;
  estado.cargando = false;
  avisarDatos();
}

export async function cargarContextos() {
  const resultados = await Promise.allSettled(servicios().map((s) => api.contexto(s)));
  servicios().forEach((servicio, i) => {
    const r = resultados[i];
    estado.contextos[servicio] = r.status === 'fulfilled' ? r.value : null;
  });
  avisarDatos();
}

/**
 * Consulta /health de cada servicio. Un servicio se da por caido solo si fallan dos intentos
 * seguidos: la primera conexion del navegador puede tardar varios segundos y no significa que
 * el servicio este detenido.
 */
export async function comprobarSalud() {
  await Promise.all(servicios().map(async (servicio) => {
    for (let intento = 1; intento <= 2; intento++) {
      const inicio = performance.now();
      try {
        await api.salud(servicio);
        estado.salud[servicio] = { ok: true, ms: Math.round(performance.now() - inicio) };
        return;
      } catch {
        estado.salud[servicio] = { ok: false };
      }
    }
  }));
  avisarSalud();
}

// ---------- Busquedas por identificador (union de datos de varios contextos, del lado del cliente) ----------

const buscar = (lista, campo) => (id) => estado[lista].find((x) => x[campo] === id);

export const tenantPorId = buscar('tenants', 'tenantId');
export const usuarioPorId = buscar('users', 'userId');
export const vehiculoPorId = buscar('vehiculos', 'vehiculoId');
export const conductorPorId = buscar('conductores', 'conductorId');
export const cargaPorId = buscar('cargas', 'cargaId');
export const pagoPorCarga = (cargaId) => estado.pagos.find((p) => p.cargaId === cargaId);
export const facturaPorCarga = (cargaId) => estado.facturas.find((f) => f.cargaId === cargaId);

const corto = (id) => (id ?? '').slice(0, 8);
export const nombreTenant = (id) => tenantPorId(id)?.nombre ?? `Organización ${corto(id)}`;
export const nombreUsuario = (id) => usuarioPorId(id)?.nombre ?? `Usuario ${corto(id)}`;
export const placaVehiculo = (id) => vehiculoPorId(id)?.placa ?? `Vehículo ${corto(id)}`;
export const nombreConductor = (id) => conductorPorId(id)?.nombre ?? `Conductor ${corto(id)}`;
export const descripcionCarga = (id) => cargaPorId(id)?.descripcion ?? `Carga ${corto(id)}`;
