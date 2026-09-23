import { ApiError, api } from '../api.js';
import {
  abrirPanel, bloque, boton, confirmar, estadoVacio, formularioModal, idChip,
  insigniaEscrow, tabla
} from '../components.js';
import { aviso, fecha, h, moneda } from '../dom.js';
import {
  cargaPorId, descripcionCarga, estado, facturaPorCarga, nombreTenant, pagoPorCarga, refrescar
} from '../store.js';

let panelAbierto = null;

export function pagos(params) {
  const acciones = [
    boton({ texto: 'Nuevo pago en Escrow', icono: 'mas', onClick: nuevoPago }),
    boton({ texto: 'Emitir factura', icono: 'recibo', variante: 'secondary', onClick: emitirFactura })
  ];

  if (estado.cargado && !estado.errores.billing && estado.pagos.length === 0 && estado.facturas.length === 0) {
    return {
      acciones,
      contenido: estadoVacio({
        icono: 'escudo',
        titulo: 'Aún no hay pagos ni facturas',
        texto: 'El generador deposita el pago en custodia y se libera al transportista cuando la carga se entrega. En este trabajo cada paso lo hace una persona; en el Trabajo 2 lo orquesta el Saga.',
        accion: boton({ texto: 'Nuevo pago en Escrow', icono: 'mas', onClick: nuevoPago })
      })
    };
  }

  return {
    acciones,
    contenido: h('div', { class: 'stack-lg' },
      bloque('Pagos en Escrow', 'Una carga admite un solo pago. Billing guarda los identificadores de la carga y de las dos organizaciones.',
        tabla({
          descripcion: 'Pagos registrados en Billing',
          filas: estado.pagos,
          alAbrir: (p) => { location.hash = `#/pagos/${p.pagoId}`; },
          columnas: [
            { titulo: 'Carga', celda: (p) => h('div', {}, h('span', { class: 'cell-title' }, descripcionCarga(p.cargaId)), h('span', { class: 'sub' }, `${nombreTenant(p.generadorTenantId)} → ${nombreTenant(p.transportistaTenantId)}`)) },
            { titulo: 'Monto', numerica: true, celda: (p) => moneda(p.monto) },
            { titulo: 'Estado', celda: (p) => insigniaEscrow(p.estadoEscrow) },
            { titulo: 'Actualizado', celda: (p) => fecha(p.actualizadoEn) }
          ]
        })),

      bloque('Facturas', 'Una carga admite una sola factura.',
        tabla({
          descripcion: 'Facturas emitidas en Billing',
          filas: estado.facturas,
          vacio: 'Aún no hay facturas.',
          columnas: [
            { titulo: 'Número', celda: (f) => h('span', { class: 'cell-title mono' }, f.numero) },
            { titulo: 'Carga', celda: (f) => descripcionCarga(f.cargaId) },
            { titulo: 'Monto', numerica: true, celda: (f) => moneda(f.monto) },
            { titulo: 'Emitida', celda: (f) => fecha(f.emitidaEn) },
            { titulo: 'Identificador', celda: (f) => idChip(f.facturaId) }
          ]
        }))),
    alMontar() {
      if (params.id) abrirDetalle(params.id);
      return undefined;
    }
  };
}

// ---------- Formularios ----------

const opcionesDeCarga = (predicado) => estado.cargas.filter(predicado).map((c) => ({
  valor: c.cargaId,
  texto: `${c.descripcion} · ${nombreTenant(c.generadorTenantId)} → ${nombreTenant(c.transportistaTenantId)}`
}));

function nuevoPago() {
  return formularioModal({
    titulo: 'Nuevo pago en Escrow',
    subtitulo: 'Billing recibe solo identificadores. No consulta a Cargo & Tracking: esa coordinación llega con el Saga del Trabajo 2.',
    textoEnvio: 'Crear pago',
    mensajeOk: 'Pago creado y fondos en custodia.',
    campos: [
      {
        nombre: 'cargaId', etiqueta: 'Carga', tipo: 'select', requerido: true,
        ayuda: 'Se listan las cargas que todavía no tienen pago.',
        sinOpciones: 'No hay cargas sin pago.',
        opciones: opcionesDeCarga((c) => !pagoPorCarga(c.cargaId))
      },
      { nombre: 'monto', etiqueta: 'Monto (COP)', tipo: 'numero', requerido: true, min: 1, paso: 'any', marcador: '850000' }
    ],
    enviar: async ({ cargaId, monto }) => {
      const carga = cargaPorId(cargaId);
      const pago = await api.billing.crearPago({
        generadorTenantId: carga.generadorTenantId,
        transportistaTenantId: carga.transportistaTenantId,
        cargaId,
        monto
      });
      await refrescar();
      location.hash = `#/pagos/${pago.pago.pagoId}`;
      return pago;
    }
  });
}

function emitirFactura() {
  return formularioModal({
    titulo: 'Emitir factura',
    subtitulo: 'La factura guarda los identificadores de la carga y de las dos organizaciones.',
    textoEnvio: 'Emitir factura',
    mensajeOk: (f) => `Factura ${f.numero} emitida.`,
    campos: [
      {
        nombre: 'cargaId', etiqueta: 'Carga', tipo: 'select', requerido: true,
        sinOpciones: 'No hay cargas sin factura.',
        opciones: opcionesDeCarga((c) => !facturaPorCarga(c.cargaId))
      },
      { nombre: 'monto', etiqueta: 'Monto (COP)', tipo: 'numero', requerido: true, min: 1, paso: 'any', marcador: '850000' }
    ],
    enviar: async ({ cargaId, monto }) => {
      const carga = cargaPorId(cargaId);
      const factura = await api.billing.emitirFactura({
        generadorTenantId: carga.generadorTenantId,
        transportistaTenantId: carga.transportistaTenantId,
        cargaId,
        monto
      });
      await refrescar();
      return factura;
    }
  });
}

// ---------- Detalle ----------

async function abrirDetalle(id) {
  if (panelAbierto) return;

  const panel = abrirPanel({
    titulo: 'Detalle del pago',
    subtitulo: 'Datos de Billing. Los nombres de organización y carga se leen de Identity y Cargo & Tracking desde esta consola.',
    cuerpo: h('div', { class: 'skeleton', style: 'height: 220px' }),
    pie: [],
    alCerrar: () => {
      panelAbierto = null;
      if (location.hash.startsWith('#/pagos/')) history.replaceState(null, '', '#/pagos');
    }
  });
  panelAbierto = panel;

  const pintar = (detalle) => {
    panel.reemplazarCuerpo(contenidoDetalle(detalle));
    panel.reemplazarPie(pieDetalle(detalle));
  };

  const cambiarEstado = async (detalle, { accion, titulo, texto, textoConfirmar, variante, mensaje }) => {
    if (!(await confirmar({ titulo, texto, textoConfirmar, variante }))) return;
    try {
      pintar(await accion());
      await refrescar();
      aviso(mensaje, 'ok');
    } catch (error) {
      if (!(error instanceof ApiError)) throw error;
      aviso(error.message, 'bad');
      if (error.estado === 409) pintar(await api.billing.pago(id));
    }
  };

  const pieDetalle = (detalle) => {
    const { pago } = detalle;
    const abierto = pago.estadoEscrow === 'EnCustodia';
    return [
      boton({ texto: 'Cerrar', variante: 'secondary', onClick: panel.cerrar }),
      boton({
        texto: 'Reembolsar al generador', variante: 'danger', deshabilitado: !abierto,
        titulo: abierto ? undefined : 'Solo un pago en custodia puede reembolsarse',
        onClick: () => cambiarEstado(detalle, {
          accion: () => api.billing.reembolsar(id),
          titulo: 'Reembolsar el pago',
          texto: `Los ${moneda(pago.monto)} volverán a ${nombreTenant(pago.generadorTenantId)}. Esta acción no se puede deshacer.`,
          textoConfirmar: 'Reembolsar', variante: 'danger', mensaje: 'Pago reembolsado al generador.'
        })
      }),
      boton({
        texto: 'Liberar al transportista', deshabilitado: !abierto,
        titulo: abierto ? undefined : 'Solo un pago en custodia puede liberarse',
        onClick: () => cambiarEstado(detalle, {
          accion: () => api.billing.liberar(id),
          titulo: 'Liberar el pago',
          texto: `Los ${moneda(pago.monto)} se entregarán a ${nombreTenant(pago.transportistaTenantId)}. Esta acción no se puede deshacer.`,
          textoConfirmar: 'Liberar', mensaje: 'Pago liberado al transportista.'
        })
      })
    ];
  };

  try {
    pintar(await api.billing.pago(id));
  } catch (error) {
    const mensaje = error instanceof ApiError && error.estado === 404 ? 'El pago no existe.' : error.message;
    panel.reemplazarCuerpo(h('div', { class: 'form-error', role: 'alert' }, mensaje));
    panel.reemplazarPie(boton({ texto: 'Cerrar', variante: 'secondary', onClick: panel.cerrar }));
  }
}

function contenidoDetalle({ pago, auditoria }) {
  const fila = (etiqueta, valor) => [h('dt', {}, etiqueta), h('dd', {}, valor)];
  const tonoDe = { EnCustodia: 'warn', Liberado: 'ok', Reembolsado: 'neutral' };

  return h('div', { class: 'stack-lg' },
    h('div', {},
      h('div', { class: 'block-title' }, h('h3', {}, moneda(pago.monto)), insigniaEscrow(pago.estadoEscrow)),
      h('dl', { class: 'kv' },
        fila('Carga', h('span', {}, descripcionCarga(pago.cargaId), ' ', idChip(pago.cargaId))),
        fila('Generador', h('span', {}, nombreTenant(pago.generadorTenantId), ' ', idChip(pago.generadorTenantId))),
        fila('Transportista', h('span', {}, nombreTenant(pago.transportistaTenantId), ' ', idChip(pago.transportistaTenantId))),
        fila('Moneda', pago.moneda),
        fila('Creado', fecha(pago.creadoEn)),
        fila('Actualizado', fecha(pago.actualizadoEn)),
        fila('Identificador', idChip(pago.pagoId)))),

    h('section', {},
      h('div', { class: 'block-title' }, h('h3', {}, 'Historial de auditoría')),
      h('p', { class: 'muted', style: 'margin-bottom: 12px' }, 'Registro de solo agregado: cada cambio del Escrow deja una fila que no se edita ni se borra.'),
      h('ol', { class: 'timeline' }, auditoria.map((a) =>
        h('li', { dataset: { tone: tonoDe[a.estadoResultante] ?? 'neutral' } },
          h('div', { class: 'when' }, fecha(a.ocurridoEn)),
          h('div', { class: 'what' }, insigniaEscrow(a.estadoResultante), ' ', a.accion))))));
}
