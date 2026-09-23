import { boton, ESTADOS_CARGA, estadoVacio, insigniaCarga, insigniaEscrow, tabla } from '../components.js';
import { fecha, h, icono, moneda, numero, plural } from '../dom.js';
import { descripcionCarga, estado, refrescar } from '../store.js';
import { cargarDatosDeEjemplo } from '../demo.js';

const ORDEN_ESTADOS = ['Pendiente', 'Asignado', 'EnTransito', 'ConNovedad', 'Entregado'];

export function resumen() {
  const listas = [estado.tenants, estado.vehiculos, estado.conductores, estado.cargas, estado.pagos];
  const hayErrores = Object.keys(estado.errores).length > 0;
  const sinDatos = estado.cargado && !hayErrores && listas.every((l) => l.length === 0);

  const acciones = [
    boton({ texto: 'Cargar datos de ejemplo', icono: 'play', variante: 'accent', onClick: cargarDatosDeEjemplo }),
    boton({ texto: 'Actualizar', icono: 'refrescar', variante: 'secondary', onClick: refrescar })
  ];

  if (sinDatos) {
    return {
      acciones,
      contenido: estadoVacio({
        icono: 'paquete',
        titulo: 'Todavía no hay datos en los cuatro servicios',
        texto: 'Cargue un conjunto de ejemplo para recorrer el ciclo completo: organizaciones, flota, cargas en distintos estados y pagos en Escrow. Cada dato se crea llamando a la API del servicio que lo posee.',
        accion: boton({ texto: 'Cargar datos de ejemplo', icono: 'play', variante: 'accent', onClick: cargarDatosDeEjemplo })
      })
    };
  }

  const generadores = estado.tenants.filter((t) => t.tenantType === 'Generador').length;
  const activas = estado.cargas.filter((c) => c.estado !== 'Entregado').length;
  const entregadas = estado.cargas.length - activas;
  const totalPor = (escrow) => estado.pagos.filter((p) => p.estadoEscrow === escrow);
  const suma = (pagos) => pagos.reduce((total, p) => total + p.monto, 0);
  const enCustodia = totalPor('EnCustodia');
  const liberados = totalPor('Liberado');

  const contenido = h('div', { class: 'stack-lg' },
    h('div', { class: 'kpis' },
      kpi('edificio', 'Organizaciones', numero(estado.tenants.length), `${generadores} generadoras · ${estado.tenants.length - generadores} transportistas`),
      kpi('camion', 'Flota', numero(estado.vehiculos.length), plural(estado.conductores.length, 'conductor', 'conductores')),
      kpi('paquete', 'Cargas activas', numero(activas), `${numero(estado.cargas.length)} en total · ${numero(entregadas)} entregadas`),
      kpi('escudo', 'En custodia', moneda(suma(enCustodia)), plural(enCustodia.length, 'pago retenido', 'pagos retenidos')),
      kpi('recibo', 'Liberado', moneda(suma(liberados)), plural(liberados.length, 'pago entregado', 'pagos entregados'))),

    h('div', { class: 'grid-2' }, tarjetaEstados(), tarjetaEscrow()),

    h('div', { class: 'grid-2' },
      h('section', {},
        h('div', { class: 'section-head' }, h('h2', {}, 'Últimas cargas')),
        tabla({
          descripcion: 'Últimas cargas registradas',
          filas: estado.cargas.slice(0, 5),
          vacio: 'Aún no hay cargas.',
          alAbrir: (c) => { location.hash = `#/cargas/${c.cargaId}`; },
          columnas: [
            { titulo: 'Carga', celda: (c) => h('div', {}, h('span', { class: 'cell-title' }, c.descripcion), h('span', { class: 'sub' }, `${c.origen} → ${c.destino}`)) },
            { titulo: 'Estado', celda: (c) => insigniaCarga(c.estado) }
          ]
        })),
      h('section', {},
        h('div', { class: 'section-head' }, h('h2', {}, 'Pagos recientes')),
        tabla({
          descripcion: 'Pagos más recientes',
          filas: estado.pagos.slice(0, 5),
          vacio: 'Aún no hay pagos.',
          alAbrir: (p) => { location.hash = `#/pagos/${p.pagoId}`; },
          columnas: [
            { titulo: 'Carga', celda: (p) => h('div', {}, h('span', { class: 'cell-title' }, descripcionCarga(p.cargaId)), h('span', { class: 'sub' }, fecha(p.creadoEn))) },
            { titulo: 'Monto', numerica: true, celda: (p) => moneda(p.monto) },
            { titulo: 'Estado', celda: (p) => insigniaEscrow(p.estadoEscrow) }
          ]
        })))
  );

  return { acciones, contenido };
}

function kpi(nombreIcono, etiqueta, valor, pie) {
  return h('div', { class: 'card kpi' },
    h('div', { class: 'kpi-label' }, icono(nombreIcono), etiqueta),
    h('div', { class: 'kpi-value' }, valor),
    h('div', { class: 'kpi-foot' }, pie));
}

function tarjetaEstados() {
  const total = estado.cargas.length;
  const cuenta = (e) => estado.cargas.filter((c) => c.estado === e).length;

  return h('section', { class: 'card card-pad' },
    h('div', { class: 'section-head' }, h('h2', {}, 'Cargas por estado')),
    total === 0
      ? h('p', { class: 'muted' }, 'Todavía no hay cargas para mostrar.')
      : [
        h('div', { class: 'stack-bar', role: 'img', 'aria-label': ORDEN_ESTADOS.map((e) => `${ESTADOS_CARGA[e].texto}: ${cuenta(e)}`).join(', ') },
          ORDEN_ESTADOS.filter((e) => cuenta(e) > 0).map((e) =>
            h('span', { title: `${ESTADOS_CARGA[e].texto}: ${cuenta(e)}`, style: `flex: ${cuenta(e)}; background: ${ESTADOS_CARGA[e].color}` }))),
        h('ul', { class: 'legend' }, ORDEN_ESTADOS.map((e) =>
          h('li', {}, h('span', { class: 'swatch', style: `background: ${ESTADOS_CARGA[e].color}` }), ESTADOS_CARGA[e].texto, h('b', {}, cuenta(e)))))
      ]);
}

function tarjetaEscrow() {
  const fila = (nombre, clave) => {
    const pagos = estado.pagos.filter((p) => p.estadoEscrow === clave);
    return h('tr', {},
      h('td', {}, nombre),
      h('td', { class: 'num' }, numero(pagos.length)),
      h('td', { class: 'num' }, moneda(pagos.reduce((t, p) => t + p.monto, 0))));
  };

  return h('section', { class: 'card card-pad' },
    h('div', { class: 'section-head' }, h('h2', {}, 'Fondos en Escrow')),
    estado.pagos.length === 0
      ? h('p', { class: 'muted' }, 'Todavía no hay pagos.')
      : h('table', {},
        h('caption', { class: 'sr-only' }, 'Fondos por estado del Escrow'),
        h('thead', {}, h('tr', {}, h('th', { scope: 'col' }, 'Estado'), h('th', { scope: 'col', class: 'num' }, 'Pagos'), h('th', { scope: 'col', class: 'num' }, 'Monto'))),
        h('tbody', {}, fila('En custodia', 'EnCustodia'), fila('Liberado', 'Liberado'), fila('Reembolsado', 'Reembolsado'))));
}
