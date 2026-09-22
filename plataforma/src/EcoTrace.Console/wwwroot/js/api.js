// Cliente HTTP de los cuatro servicios. La consola es solo un cliente: cada llamada va al
// servicio dueño del dato, y cuando una pantalla necesita datos de varios contextos los une aqui,
// en el navegador, usando unicamente identificadores. Ningun servicio llama a otro.

const NOMBRES = {
  identity: 'Identity',
  fleet: 'Fleet Management',
  cargo: 'Cargo & Tracking',
  billing: 'Billing & Escrow'
};

let bases = {};

export const nombreServicio = (servicio) => NOMBRES[servicio];
export const servicios = () => Object.keys(NOMBRES);
export const urlBase = (servicio) => bases[servicio];

export async function iniciarApi() {
  const respuesta = await fetch('/config.json');
  bases = (await respuesta.json()).servicios;
}

/** Error de una llamada: con estado HTTP, ProblemDetails y un mensaje listo para mostrar. */
export class ApiError extends Error {
  constructor(estado, problema, servicio) {
    super(mensajeDe(estado, problema, servicio));
    this.estado = estado;
    this.problema = problema;
    this.servicio = servicio;
    this.camposConError = camposDe(problema);
  }

  get sinConexion() {
    return this.estado === 0;
  }
}

function mensajeDe(estado, problema, servicio) {
  if (estado === 0) {
    return `No se pudo conectar con ${NOMBRES[servicio]}. Compruebe que el servicio está en ejecución.`;
  }
  return problema?.detail ?? problema?.title ?? `${NOMBRES[servicio]} respondió con el estado ${estado}.`;
}

function camposDe(problema) {
  const errores = problema?.errors;
  if (!errores) return {};
  return Object.fromEntries(Object.entries(errores).map(([campo, mensajes]) => [campo.toLowerCase(), mensajes[0]]));
}

async function pedir(servicio, ruta, { metodo = 'GET', cuerpo, espera = 8000 } = {}) {
  const control = new AbortController();
  const temporizador = setTimeout(() => control.abort(), espera);
  try {
    const respuesta = await fetch(bases[servicio] + ruta, {
      method: metodo,
      headers: cuerpo === undefined ? undefined : { 'Content-Type': 'application/json' },
      body: cuerpo === undefined ? undefined : JSON.stringify(cuerpo),
      signal: control.signal
    });
    const texto = await respuesta.text();
    const datos = texto ? JSON.parse(texto) : null;
    if (!respuesta.ok) throw new ApiError(respuesta.status, datos, servicio);
    return datos;
  } catch (error) {
    if (error instanceof ApiError) throw error;
    throw new ApiError(0, null, servicio);
  } finally {
    clearTimeout(temporizador);
  }
}

const consulta = (params) => {
  const usados = Object.entries(params).filter(([, v]) => v != null && v !== '');
  return usados.length ? `?${new URLSearchParams(usados)}` : '';
};

export const api = {
  salud: (servicio) => pedir(servicio, '/health', { espera: 5000 }),
  contexto: (servicio) => pedir(servicio, '/api/_meta/contexto'),

  identity: {
    tenants: (tipo) => pedir('identity', `/api/tenants${consulta({ tipo })}`),
    crearTenant: (cuerpo) => pedir('identity', '/api/tenants', { metodo: 'POST', cuerpo }),
    users: (tenantId) => pedir('identity', `/api/users${consulta({ tenantId })}`),
    crearUser: (cuerpo) => pedir('identity', '/api/users', { metodo: 'POST', cuerpo }),
    roles: () => pedir('identity', '/api/roles')
  },

  fleet: {
    vehiculos: (tenantId) => pedir('fleet', `/api/vehiculos${consulta({ tenantId })}`),
    crearVehiculo: (cuerpo) => pedir('fleet', '/api/vehiculos', { metodo: 'POST', cuerpo }),
    conductores: (tenantId) => pedir('fleet', `/api/conductores${consulta({ tenantId })}`),
    crearConductor: (cuerpo) => pedir('fleet', '/api/conductores', { metodo: 'POST', cuerpo })
  },

  cargo: {
    cargas: (filtros = {}) => pedir('cargo', `/api/cargas${consulta(filtros)}`),
    carga: (id) => pedir('cargo', `/api/cargas/${id}`),
    crearCarga: (cuerpo) => pedir('cargo', '/api/cargas', { metodo: 'POST', cuerpo }),
    asignar: (id, cuerpo) => pedir('cargo', `/api/cargas/${id}/asignacion`, { metodo: 'POST', cuerpo }),
    seguimiento: (id, cuerpo) => pedir('cargo', `/api/cargas/${id}/seguimientos`, { metodo: 'POST', cuerpo })
  },

  billing: {
    pagos: (filtros = {}) => pedir('billing', `/api/pagos${consulta(filtros)}`),
    pago: (id) => pedir('billing', `/api/pagos/${id}`),
    crearPago: (cuerpo) => pedir('billing', '/api/pagos', { metodo: 'POST', cuerpo }),
    liberar: (id) => pedir('billing', `/api/pagos/${id}/liberar`, { metodo: 'POST' }),
    reembolsar: (id) => pedir('billing', `/api/pagos/${id}/reembolso`, { metodo: 'POST' }),
    facturas: (filtros = {}) => pedir('billing', `/api/facturas${consulta(filtros)}`),
    emitirFactura: (cuerpo) => pedir('billing', '/api/facturas', { metodo: 'POST', cuerpo })
  }
};
