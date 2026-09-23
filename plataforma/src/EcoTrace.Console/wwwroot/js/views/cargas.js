import { ApiError, api } from '../api.js';
import {
  abrirPanel, boton, ESTADOS_CARGA, estadoVacio, formularioModal, idChip, insigniaCarga,
  SIGUIENTES_CARGA, tabla
} from '../components.js';
import { aviso, fecha, h, numero } from '../dom.js';
import {
  estado, nombreConductor, nombreTenant, placaVehiculo, refrescar, repintar
} from '../store.js';

let filtro = 'todas';
let panelAbierto = null;

// Color del punto de cada evento en la linea de tiempo.
const TONO_LINEA = { Pendiente: 'neutral', ConNovedad: 'warn', Entregado: 'ok' };

const generadores = () => estado.tenants.filter((t) => t.tenantType === 'Generador');
const transportistas = () => estado.tenants.filter((t) => t.tenantType === 'Transportista');

export function cargas(params) {
  const acciones = [boton({ texto: 'Nueva carga', icono: 'mas', onClick: nuevaCarga })];

  if (estado.cargado && !estado.errores.cargo && estado.cargas.length === 0) {
    return {
      acciones,
      contenido: estadoVacio({
        icono: 'paquete',
        titulo: 'Aún no hay cargas',
        texto: 'Una carga involucra a dos organizaciones: la generadora que la produce y la transportista que la mueve. Se crea en estado Pendiente y se asigna después a un vehículo y un conductor.',
        accion: boton({ texto: 'Nueva carga', icono: 'mas', onClick: nuevaCarga })
      })
    };
  }

  const visibles = filtro === 'todas' ? estado.cargas : estado.cargas.filter((c) => c.estado === filtro);
  const cuenta = (clave) => estado.cargas.filter((c) => c.estado === clave).length;

  const chip = (clave, texto, total) => h('button', {
    type: 'button', class: 'filter', 'aria-pressed': String(filtro === clave),
    onClick: () => { filtro = clave; repintar(); }
  }, texto, h('span', {}, total));

  return {
    acciones,
    contenido: h('div', {},
      h('div', { class: 'filters', role: 'group', 'aria-label': 'Filtrar por estado' },
        chip('todas', 'Todas', estado.cargas.length),
        Object.entries(ESTADOS_CARGA).map(([clave, info]) => chip(clave, info.texto, cuenta(clave)))),
      tabla({
        descripcion: 'Cargas registradas en Cargo & Tracking',
        filas: visibles,
        vacio: 'Ninguna carga está en este estado.',
        alAbrir: (c) => { location.hash = `#/cargas/${c.cargaId}`; },
        columnas: [
          { titulo: 'Carga', celda: (c) => h('div', {}, h('span', { class: 'cell-title' }, c.descripcion), h('span', { class: 'sub' }, `${c.origen} → ${c.destino}`)) },
          { titulo: 'Peso', numerica: true, celda: (c) => `${numero(c.pesoKg)} kg` },
          { titulo: 'Generador', celda: (c) => nombreTenant(c.generadorTenantId) },
          { titulo: 'Transportista', celda: (c) => nombreTenant(c.transportistaTenantId) },
          { titulo: 'Estado', celda: (c) => insigniaCarga(c.estado) },
          { titulo: 'Creada', celda: (c) => fecha(c.creadoEn) }
        ]
      })),
    alMontar() {
      if (params.id) abrirDetalle(params.id);
      return undefined;
    }
  };
}

// ---------- Nueva carga ----------

function nuevaCarga() {
  return formularioModal({
    titulo: 'Nueva carga',
    subtitulo: 'Cargo & Tracking guarda los identificadores de las dos organizaciones, sin consultar a Identity.',
    textoEnvio: 'Crear carga',
    mensajeOk: 'Carga creada en estado Pendiente.',
    campos: [
      {
        nombre: 'generadorTenantId', etiqueta: 'Generador', tipo: 'select', requerido: true, mitad: true,
        sinOpciones: 'Cree una organización Generadora.',
        opciones: generadores().map((t) => ({ valor: t.tenantId, texto: t.nombre }))
      },
      {
        nombre: 'transportistaTenantId', etiqueta: 'Transportista', tipo: 'select', requerido: true, mitad: true,
        sinOpciones: 'Cree una organización Transportista.',
        opciones: transportistas().map((t) => ({ valor: t.tenantId, texto: t.nombre }))
      },
      { nombre: 'descripcion', etiqueta: 'Descripción', requerido: true, marcador: 'Residuos industriales no peligrosos' },
      { nombre: 'origen', etiqueta: 'Origen', requerido: true, mitad: true, marcador: 'Cali' },
      { nombre: 'destino', etiqueta: 'Destino', requerido: true, mitad: true, marcador: 'Popayán' },
      { nombre: 'pesoKg', etiqueta: 'Peso (kg)', tipo: 'numero', requerido: true, min: 1, max: 80000 }
    ],
    enviar: async (datos) => {
      const carga = await api.cargo.crearCarga(datos);
      await refrescar();
      location.hash = `#/cargas/${carga.cargaId}`;
      return carga;
    }
  });
}

// ---------- Detalle ----------

async function abrirDetalle(id) {
  if (panelAbierto) return;

  const panel = abrirPanel({
    titulo: 'Detalle de la carga',
    subtitulo: 'Datos de Cargo & Tracking. Los nombres de vehículo y conductor se leen de Fleet Management desde esta consola.',
    cuerpo: h('div', { class: 'skeleton', style: 'height: 220px' }),
    pie: [],
    alCerrar: () => {
      panelAbierto = null;
      if (location.hash.startsWith('#/cargas/')) history.replaceState(null, '', '#/cargas');
    }
  });
  panelAbierto = panel;

  const pintar = (detalle) => {
    panel.reemplazarCuerpo(contenidoDetalle(detalle));
    panel.reemplazarPie(pieDetalle(detalle));
  };

  const recargar = async () => {
    try {
      pintar(await api.cargo.carga(id));
    } catch (error) {
      const mensaje = error instanceof ApiError && error.estado === 404 ? 'La carga no existe.' : error.message;
      panel.reemplazarCuerpo(h('div', { class: 'form-error', role: 'alert' }, mensaje));
      panel.reemplazarPie(boton({ texto: 'Cerrar', variante: 'secondary', onClick: panel.cerrar }));
    }
  };

  const pieDetalle = (detalle) => {
    const { carga } = detalle;
    const botones = [];
    if (carga.estado === 'Pendiente') {
      botones.push(boton({ texto: 'Asignar vehículo y conductor', icono: 'camion', onClick: () => asignar(detalle) }));
    }
    if (SIGUIENTES_CARGA[carga.estado]) {
      botones.push(boton({ texto: 'Registrar seguimiento', icono: 'mas', onClick: () => seguimiento(detalle) }));
    }
    if (carga.estado === 'Entregado') {
      botones.push(boton({ texto: 'Ir a Facturación y Escrow', icono: 'recibo', onClick: () => { panel.cerrar(); location.hash = '#/pagos'; } }));
    }
    return [boton({ texto: 'Cerrar', variante: 'secondary', onClick: panel.cerrar }), ...botones];
  };

  const asignar = ({ carga }) => formularioModal({
    titulo: 'Asignar vehículo y conductor',
    subtitulo: `Flota de ${nombreTenant(carga.transportistaTenantId)}. La carga guarda solo los identificadores.`,
    textoEnvio: 'Asignar',
    mensajeOk: 'Carga asignada.',
    campos: [
      {
        nombre: 'vehiculoId', etiqueta: 'Vehículo', tipo: 'select', requerido: true,
        sinOpciones: 'Este transportista no tiene vehículos registrados.',
        opciones: estado.vehiculos.filter((v) => v.tenantId === carga.transportistaTenantId)
          .map((v) => ({ valor: v.vehiculoId, texto: `${v.placa} · ${numero(v.capacidadKg)} kg` }))
      },
      {
        nombre: 'conductorId', etiqueta: 'Conductor', tipo: 'select', requerido: true,
        sinOpciones: 'Este transportista no tiene conductores registrados.',
        opciones: estado.conductores.filter((c) => c.tenantId === carga.transportistaTenantId)
          .map((c) => ({ valor: c.conductorId, texto: `${c.nombre} · ${c.licencia}` }))
      }
    ],
    enviar: async (datos) => {
      const detalle = await api.cargo.asignar(id, datos);
      pintar(detalle);
      await refrescar();
      return detalle;
    }
  });

  const seguimiento = ({ carga }) => formularioModal({
    titulo: 'Registrar seguimiento',
    subtitulo: `Estado actual: ${ESTADOS_CARGA[carga.estado].texto}. Solo se ofrecen las transiciones válidas.`,
    textoEnvio: 'Registrar',
    mensajeOk: 'Seguimiento registrado.',
    campos: [
      {
        nombre: 'estado', etiqueta: 'Nuevo estado', tipo: 'select', requerido: true,
        opciones: SIGUIENTES_CARGA[carga.estado].map((e) => ({ valor: e, texto: ESTADOS_CARGA[e].texto }))
      },
      { nombre: 'ubicacion', etiqueta: 'Ubicación', requerido: true, marcador: 'Peaje Villa Rica, vía Panamericana' },
      { nombre: 'nota', etiqueta: 'Nota', tipo: 'area', ayuda: 'Obligatoria cuando el estado es Con novedad.' }
    ],
    enviar: async (datos) => {
      const detalle = await api.cargo.seguimiento(id, { ...datos, nota: datos.nota || null });
      pintar(detalle);
      await refrescar();
      if (datos.estado === 'Entregado') aviso('La carga fue entregada. Ya puede crear su pago en Facturación y Escrow.', 'info');
      return detalle;
    }
  });

  await recargar();
}

function contenidoDetalle({ carga, asignacion, seguimientos }) {
  const fila = (etiqueta, valor) => [h('dt', {}, etiqueta), h('dd', {}, valor)];

  return h('div', { class: 'stack-lg' },
    h('div', {},
      h('div', { class: 'block-title' }, h('h3', {}, carga.descripcion), insigniaCarga(carga.estado)),
      h('dl', { class: 'kv' },
        fila('Ruta', `${carga.origen} → ${carga.destino}`),
        fila('Peso', `${numero(carga.pesoKg)} kg`),
        fila('Generador', h('span', {}, nombreTenant(carga.generadorTenantId), ' ', idChip(carga.generadorTenantId))),
        fila('Transportista', h('span', {}, nombreTenant(carga.transportistaTenantId), ' ', idChip(carga.transportistaTenantId))),
        fila('Creada', fecha(carga.creadoEn)),
        fila('Identificador', idChip(carga.cargaId)))),

    h('section', {},
      h('div', { class: 'block-title' }, h('h3', {}, 'Vehículo y conductor')),
      asignacion
        ? h('dl', { class: 'kv' },
          fila('Vehículo', h('span', {}, placaVehiculo(asignacion.vehiculoId), ' ', idChip(asignacion.vehiculoId))),
          fila('Conductor', h('span', {}, nombreConductor(asignacion.conductorId), ' ', idChip(asignacion.conductorId))),
          fila('Asignada', fecha(asignacion.asignadaEn)))
        : h('p', { class: 'muted' }, 'Todavía no tiene vehículo ni conductor asignados.')),

    h('section', {},
      h('div', { class: 'block-title' }, h('h3', {}, 'Seguimiento')),
      h('ol', { class: 'timeline' }, seguimientos.map((s) =>
        h('li', { dataset: { tone: TONO_LINEA[s.estado] ?? 'info' } },
          h('div', { class: 'when' }, fecha(s.registradoEn)),
          h('div', { class: 'what' }, insigniaCarga(s.estado), ' ', s.ubicacion),
          s.nota && h('div', { class: 'muted' }, s.nota))))));
}
