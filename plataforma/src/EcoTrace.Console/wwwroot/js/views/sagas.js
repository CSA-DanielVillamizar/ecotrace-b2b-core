import { ApiError, api } from '../api.js';
import {
  abrirPanel, bloque, boton, estadoVacio, idChip, insigniaEscrow, insigniaOutbox, insigniaPaso,
  insigniaSaga, insignia, tabla
} from '../components.js';
import { aviso, fecha, h } from '../dom.js';
import {
  descripcionCarga, estado, nombreTenant, pagoPorCarga, refrescar, repintar
} from '../store.js';

let panelAbierto = null;

// Estado de la simulacion de fallos de Fleet. Se lee al abrir la vista y despues de cada cambio.
const simulacion = { estado: 'desconocido', restantes: 0 };

const NOMBRE_DEL_PASO = {
  AutorizarPago: { texto: 'Autorizar el pago', servicio: 'Identity' },
  LiberarFondos: { texto: 'Liberar los fondos', servicio: 'Billing' },
  LiberarRecursos: { texto: 'Liberar vehículo y conductor', servicio: 'Fleet Management' },
  RegistrarAuditoria: { texto: 'Registrar la auditoría', servicio: 'Billing' }
};

const hayTrabajoPendiente = () =>
  estado.sagas.some((s) => s.estado === 'EnCurso' || s.estado === 'Compensando')
  || estado.outbox.some((m) => m.estado === 'Pendiente');

export function sagas(params) {
  const acciones = [boton({ texto: 'Preparar envío de demostración', icono: 'mas', onClick: prepararEnvio })];

  // Una carga entregada deja de estar EnTransito, asi que basta con ese estado para no ofrecerla dos veces.
  const listos = estado.cargas.filter((c) => c.estado === 'EnTransito'
    && pagoPorCarga(c.cargaId)?.estadoEscrow === 'EnCustodia');

  return {
    acciones,
    contenido: h('div', { class: 'stack-lg' },
      bloque('Envíos listos para entregar',
        'Cargas en tránsito con su pago en custodia. Registrar la entrega dispara el evento y, con él, el Saga.',
        tabla({
          descripcion: 'Cargas en tránsito con pago en custodia',
          filas: listos,
          vacio: 'No hay envíos listos. Prepare uno de demostración con el botón de arriba.',
          columnas: [
            { titulo: 'Carga', celda: (c) => h('div', {}, h('span', { class: 'cell-title' }, c.descripcion), h('span', { class: 'sub' }, `${c.origen} → ${c.destino}`)) },
            { titulo: 'Pago', celda: (c) => insigniaEscrow(pagoPorCarga(c.cargaId).estadoEscrow) },
            { titulo: 'Acción', celda: (c) => boton({ texto: 'Registrar entrega', pequeno: true, onClick: (e) => entregar(c, e.currentTarget) }) }
          ]
        })),

      bloqueSimulacion(),

      bloque('Sagas de liberación de pago', 'Un Saga por pago. Billing lo orquesta y guarda el estado de cada paso.',
        tabla({
          descripcion: 'Sagas de liberación de pago en Billing',
          filas: estado.sagas,
          alAbrir: (s) => { location.hash = `#/sagas/${s.sagaId}`; },
          vacio: 'Aún no hay Sagas. Registre una entrega para iniciar el primero.',
          columnas: [
            { titulo: 'Carga', celda: (s) => h('div', {}, h('span', { class: 'cell-title' }, descripcionCarga(s.cargaId)), h('span', { class: 'sub' }, resumenDePasos(s))) },
            { titulo: 'Estado', celda: (s) => insigniaSaga(s.estado) },
            { titulo: 'Motivo', celda: (s) => s.motivo ? h('span', { class: 'sub' }, s.motivo) : h('span', { class: 'muted' }, '—') },
            { titulo: 'Actualizado', celda: (s) => fecha(s.actualizadoEn) }
          ]
        })),

      bloque('Outbox de Cargo & Tracking', 'Los eventos que Cargo guardó junto con la entrega y todavía envía, o ya envió, a Billing.',
        tabla({
          descripcion: 'Mensajes del Outbox de Cargo & Tracking',
          filas: estado.outbox,
          vacio: 'El Outbox está vacío.',
          columnas: [
            { titulo: 'Evento', celda: (m) => h('div', {}, h('span', { class: 'cell-title' }, m.tipo), idChip(m.eventoId)) },
            { titulo: 'Estado', celda: (m) => insigniaOutbox(m.estado) },
            { titulo: 'Intentos', numerica: true, celda: (m) => m.intentos },
            { titulo: 'Último error', celda: (m) => m.ultimoError ? h('span', { class: 'sub' }, m.ultimoError) : h('span', { class: 'muted' }, '—') },
            { titulo: 'Acción', celda: (m) => m.estado === 'Muerto'
              ? boton({ texto: 'Reprocesar', pequeno: true, variante: 'secondary', onClick: (e) => reprocesar(m, e.currentTarget) })
              : null }
          ]
        }))),

    alMontar() {
      leerSimulacion();
      if (params.id) abrirDetalle(params.id);

      // Mientras haya un Saga en marcha o un mensaje sin entregar, la lista se actualiza sola.
      const temporizador = hayTrabajoPendiente() ? setTimeout(refrescar, 1200) : null;
      return () => clearTimeout(temporizador);
    }
  };
}

const resumenDePasos = (saga) => {
  const hechos = saga.pasos.filter((p) => p.estado === 'Completado' || p.estado === 'Compensado').length;
  return `${hechos} de ${saga.pasos.length} pasos`;
};

// ---------- Simulación de fallos ----------

function bloqueSimulacion() {
  const activa = simulacion.estado === 'ok';
  const texto = {
    ok: simulacion.restantes > 0
      ? `Fleet responderá 503 a las próximas ${simulacion.restantes} liberaciones.`
      : 'Fleet responde con normalidad.',
    apagada: 'La simulación está apagada en Fleet. Arránquelo con Simulacion__Habilitada=true (run-all y docker compose ya lo hacen).',
    desconocido: 'Consultando a Fleet…'
  }[simulacion.estado];

  return bloque('Simulación de fallos en Fleet',
    'Hace fallar a propósito la liberación de vehículo y conductor para ver el reintento y la compensación.',
    h('div', { class: 'stack' },
      h('p', { role: 'status' }, insignia(activa && simulacion.restantes > 0 ? 'Fallo armado' : 'Sin fallos', activa && simulacion.restantes > 0 ? 'bad' : 'neutral'), ' ', texto),
      h('div', { class: 'row-actions' },
        boton({ texto: 'Hacer fallar las próximas liberaciones', variante: 'danger', deshabilitado: !activa, onClick: (e) => armar(100, e.currentTarget) }),
        boton({ texto: 'Restablecer', variante: 'secondary', deshabilitado: !activa, onClick: (e) => armar(0, e.currentTarget) }))));
}

let leyendoSimulacion = false;

// Se llama en cada dibujo de la vista. Solo vuelve a dibujar si lo leido cambio, para no entrar en un ciclo.
async function leerSimulacion() {
  if (leyendoSimulacion) return;
  leyendoSimulacion = true;
  const antes = JSON.stringify(simulacion);
  try {
    const fallos = await api.fleet.fallos();
    simulacion.estado = 'ok';
    simulacion.restantes = fallos.liberaciones ?? 0;
  } catch (error) {
    simulacion.estado = error instanceof ApiError && error.estado === 404 ? 'apagada' : 'desconocido';
  } finally {
    leyendoSimulacion = false;
  }
  if (JSON.stringify(simulacion) !== antes) repintar();
}

async function armar(cantidad, elBoton) {
  elBoton.disabled = true;
  try {
    await api.fleet.armarFallos(cantidad);
    aviso(cantidad > 0 ? 'Fleet fallará en las próximas liberaciones.' : 'Fleet vuelve a responder con normalidad.', 'ok');
  } catch (error) {
    if (!(error instanceof ApiError)) throw error;
    aviso(error.message, 'bad');
  }
  await leerSimulacion();
}

// ---------- Acciones ----------

async function entregar(carga, elBoton) {
  elBoton.disabled = true;
  try {
    await api.cargo.seguimiento(carga.cargaId, { estado: 'Entregado', ubicacion: carga.destino });
    aviso('Entrega registrada. El evento quedó en el Outbox.', 'ok');
  } catch (error) {
    if (!(error instanceof ApiError)) throw error;
    aviso(error.message, 'bad');
  }
  await refrescar();
}

async function reprocesar(mensaje, elBoton) {
  elBoton.disabled = true;
  try {
    await api.cargo.reprocesar(mensaje.eventoId);
    aviso('Mensaje devuelto a la cola. El publicador lo intentará de nuevo.', 'ok');
  } catch (error) {
    if (!(error instanceof ApiError)) throw error;
    aviso(error.message, 'bad');
  }
  await refrescar();
}

/** Deja un envío completo en tránsito, con vehículo y conductor reservados y el pago en custodia. */
async function prepararEnvio(evento) {
  const elBoton = evento?.currentTarget;
  if (elBoton) elBoton.disabled = true;

  const sufijo = Math.random().toString(36).slice(2, 8);
  const placa = `D${sufijo.toUpperCase()}`.slice(0, 7);
  try {
    const generador = await api.identity.crearTenant({ nombre: `Generadora ${sufijo}`, tenantType: 'Generador' });
    const transportista = await api.identity.crearTenant({ nombre: `Transportes ${sufijo}`, tenantType: 'Transportista' });
    const supervisor = await api.identity.crearUser({
      tenantId: transportista.tenantId, role: 'Supervisor', nombre: 'Supervisor de demostración', email: `demo.${sufijo}@ejemplo.co`
    });
    const vehiculo = await api.fleet.crearVehiculo({
      tenantId: transportista.tenantId, registradoPorUserId: supervisor.userId, placa, capacidadKg: 6000
    });
    const conductor = await api.fleet.crearConductor({
      tenantId: transportista.tenantId, registradoPorUserId: supervisor.userId, nombre: 'Conductor de demostración', licencia: `C2-${Math.floor(Math.random() * 900000 + 100000)}`
    });
    const carga = await api.cargo.crearCarga({
      generadorTenantId: generador.tenantId, transportistaTenantId: transportista.tenantId,
      descripcion: `Residuos de demostración ${sufijo}`, origen: 'Cali', destino: 'Popayán', pesoKg: 3200
    });
    await api.cargo.asignar(carga.cargaId, { vehiculoId: vehiculo.vehiculoId, conductorId: conductor.conductorId });
    await api.fleet.reservar({ cargaId: carga.cargaId, vehiculoId: vehiculo.vehiculoId, conductorId: conductor.conductorId });
    await api.cargo.seguimiento(carga.cargaId, { estado: 'EnTransito', ubicacion: 'Santander de Quilichao' });
    await api.billing.crearPago({
      generadorTenantId: generador.tenantId, transportistaTenantId: transportista.tenantId, cargaId: carga.cargaId, monto: 850000
    });
    aviso('Envío de demostración listo para entregar.', 'ok');
  } catch (error) {
    if (!(error instanceof ApiError)) throw error;
    aviso(error.message, 'bad');
  }
  await refrescar();
}

// ---------- Detalle ----------

const TONO_DEL_PASO = { Pendiente: 'neutral', Completado: 'ok', Fallido: 'bad', Compensado: 'warn' };

async function abrirDetalle(id) {
  if (panelAbierto) return;

  let temporizador;
  const panel = abrirPanel({
    titulo: 'Detalle del Saga',
    subtitulo: 'Estado guardado en Billing. Se actualiza solo mientras el Saga está en marcha.',
    cuerpo: h('div', { class: 'skeleton', style: 'height: 220px' }),
    pie: [],
    alCerrar: () => {
      clearTimeout(temporizador);
      panelAbierto = null;
      if (location.hash.startsWith('#/sagas/')) history.replaceState(null, '', '#/sagas');
    }
  });
  panelAbierto = panel;

  const cargar = async () => {
    try {
      const saga = await api.billing.saga(id);
      panel.reemplazarCuerpo(contenidoDetalle(saga));
      panel.reemplazarPie(boton({ texto: 'Cerrar', variante: 'secondary', onClick: panel.cerrar }));
      if (saga.estado === 'EnCurso' || saga.estado === 'Compensando') temporizador = setTimeout(cargar, 900);
      else refrescar();
    } catch (error) {
      const mensaje = error instanceof ApiError && error.estado === 404 ? 'El Saga no existe.' : error.message;
      panel.reemplazarCuerpo(h('div', { class: 'form-error', role: 'alert' }, mensaje));
      panel.reemplazarPie(boton({ texto: 'Cerrar', variante: 'secondary', onClick: panel.cerrar }));
    }
  };
  await cargar();
}

function contenidoDetalle(saga) {
  const fila = (etiqueta, valor) => [h('dt', {}, etiqueta), h('dd', {}, valor)];
  const pago = estado.pagos.find((p) => p.pagoId === saga.pagoId);

  return h('div', { class: 'stack-lg' },
    h('div', {},
      h('div', { class: 'block-title' }, h('h3', {}, descripcionCarga(saga.cargaId)), insigniaSaga(saga.estado)),
      saga.motivo && h('div', { class: 'banner', role: 'status', dataset: { tone: saga.estado === 'Completada' ? 'info' : 'bad' }, style: 'margin-bottom: 12px' }, h('div', {}, saga.motivo)),
      h('dl', { class: 'kv' },
        fila('Pago', pago
          ? h('span', {}, insigniaEscrow(pago.estadoEscrow), ' ', h('a', { href: `#/pagos/${pago.pagoId}` }, 'Ver pago'))
          : idChip(saga.pagoId)),
        pago && fila('Organizaciones', `${nombreTenant(pago.generadorTenantId)} → ${nombreTenant(pago.transportistaTenantId)}`),
        fila('Correlación', h('span', { class: 'mono' }, saga.correlationId)),
        fila('Creado', fecha(saga.creadoEn)),
        fila('Actualizado', fecha(saga.actualizadoEn)),
        fila('Identificador', idChip(saga.sagaId)))),

    h('section', {},
      h('div', { class: 'block-title' }, h('h3', {}, 'Pasos')),
      h('p', { class: 'muted', style: 'margin-bottom: 12px' }, 'Si un paso falla y se agotan sus intentos, los pasos ya completados se compensan en orden inverso.'),
      h('ol', { class: 'timeline' }, saga.pasos.map((p) => {
        const nombre = NOMBRE_DEL_PASO[p.nombre] ?? { texto: p.nombre, servicio: '' };
        const intentos = p.intentos + p.intentosCompensacion > 0
          ? ` · ${p.intentos} intento(s) fallido(s)${p.intentosCompensacion > 0 ? `, ${p.intentosCompensacion} al compensar` : ''}`
          : '';
        return h('li', { dataset: { tone: TONO_DEL_PASO[p.estado] ?? 'neutral' } },
          h('div', { class: 'when' }, `${p.orden}. ${nombre.servicio}${intentos}`),
          h('div', { class: 'what' }, insigniaPaso(p.estado), ' ', h('strong', {}, nombre.texto), p.detalle ? h('span', { class: 'sub', style: 'display:block;margin-top:4px' }, p.detalle) : null));
      }))));
}
