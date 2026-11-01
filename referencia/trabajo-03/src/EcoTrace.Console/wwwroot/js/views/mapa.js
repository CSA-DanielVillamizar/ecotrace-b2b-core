import { nombreServicio, servicios, urlBase } from '../api.js';
import { h, icono } from '../dom.js';
import { cargarContextos, estado } from '../store.js';

const SVG_NS = 'http://www.w3.org/2000/svg';

// Nombre con el que cada contexto se identifica dentro de [ReferenciaExterna(...)].
const CLAVE_CONTEXTO = { identity: 'Identity', fleet: 'FleetManagement', cargo: 'CargoTracking', billing: 'Billing' };
const SERVICIO_POR_CONTEXTO = Object.fromEntries(Object.entries(CLAVE_CONTEXTO).map(([servicio, clave]) => [clave, servicio]));

export function mapa() {
  if (Object.keys(estado.contextos).length === 0) cargarContextos();

  const cargados = Object.keys(estado.contextos).length > 0;
  const tarjetas = servicios().map(tarjeta);

  const svg = document.createElementNS(SVG_NS, 'svg');
  svg.setAttribute('class', 'ctx-svg');
  svg.setAttribute('aria-hidden', 'true');

  const contenedor = h('div', { class: 'ctx-map' }, svg, tarjetas);

  const contenido = h('div', { class: 'stack-lg' },
    h('div', { class: 'card rule-card' },
      icono('enlace'),
      h('p', {},
        h('strong', {}, 'Regla de oro del ADR 0001: '),
        'un módulo referencia a otro únicamente por su identificador. Cada contexto tiene su propia base de datos y ningún módulo consulta la de otro. ',
        'Este diagrama no está dibujado a mano: cada servicio describe su modelo de EF Core y sus atributos ',
        h('code', {}, '[ReferenciaExterna]'), ' en ', h('code', {}, '/api/_meta/contexto'), ', y la consola los lee.')),
    cargados ? contenedor : h('div', { class: 'empty-inline' }, 'Leyendo los contextos…'));

  return {
    contenido,
    alMontar() {
      if (!cargados) return undefined;
      const dibujar = () => dibujarConexiones(contenedor, svg);
      const observador = new ResizeObserver(dibujar);
      observador.observe(contenedor);
      requestAnimationFrame(dibujar);
      return () => observador.disconnect();
    }
  };
}

function tarjeta(servicio) {
  const meta = estado.contextos[servicio];
  const clave = CLAVE_CONTEXTO[servicio];

  if (!meta) {
    return h('section', { class: 'card ctx-card', dataset: { ctx: clave } },
      h('h2', {}, nombreServicio(servicio)),
      h('div', { class: 'ctx-off' }, `Sin conexión con ${urlBase(servicio)}`));
  }

  return h('section', { class: 'card ctx-card', dataset: { ctx: clave }, 'aria-label': `Contexto ${nombreServicio(servicio)}` },
    h('div', { class: 'ctx-head' },
      h('div', {},
        h('h2', {}, nombreServicio(servicio)),
        h('span', { class: 'ctx-db' }, icono('base'), `${meta.baseDeDatos} · ${meta.motor}`))),
    meta.entidades.map(entidad));
}

function entidad(e) {
  const internas = e.relacionesInternas.length;
  return h('div', { class: 'entity' },
    h('div', { class: 'entity-name' }, e.nombre, h('small', {}, e.tabla)),
    e.referenciasExternas.length > 0 && h('ul', { class: 'refs', 'aria-label': `Referencias externas de ${e.nombre}` },
      e.referenciasExternas.map((r) => h('li', { class: 'ref', title: r.descripcion },
        icono('enlace'), h('code', {}, r.campo), `→ ${nombreDelContexto(r.contexto)}`))),
    h('p', { class: 'fk-note' },
      internas === 0 ? 'Sin claves foráneas dentro del contexto.' : `${internas} ${internas === 1 ? 'clave foránea interna' : 'claves foráneas internas'}: ${e.relacionesInternas.map((r) => `${r.campo} → ${r.destino}`).join(', ')}.`),
    h('details', { class: 'props' },
      h('summary', {}, `Propiedades (${e.propiedades.length})`),
      h('table', {},
        h('thead', {}, h('tr', {}, h('th', { scope: 'col' }, 'Nombre'), h('th', { scope: 'col' }, 'Tipo'))),
        h('tbody', {}, e.propiedades.map((p) => h('tr', {}, h('td', { class: 'mono' }, p.nombre), h('td', { class: 'muted' }, p.tipo)))))));
}

const nombreDelContexto = (clave) => nombreServicio(SERVICIO_POR_CONTEXTO[clave]) ?? clave;

// ---------- Conexiones entre tarjetas ----------

function dibujarConexiones(contenedor, svg) {
  svg.replaceChildren();
  const caja = contenedor.getBoundingClientRect();
  const rect = (clave) => {
    const el = contenedor.querySelector(`[data-ctx="${clave}"]`);
    if (!el) return null;
    const r = el.getBoundingClientRect();
    return { izq: r.left - caja.left, der: r.right - caja.left, arriba: r.top - caja.top, abajo: r.bottom - caja.top, x: r.left - caja.left + r.width / 2 };
  };

  svg.setAttribute('viewBox', `0 0 ${caja.width} ${caja.height}`);
  svg.append(marcador());

  // Agrupa los campos por (origen -> destino) para dibujar una sola flecha por par de contextos.
  const aristas = new Map();
  for (const servicio of servicios()) {
    const meta = estado.contextos[servicio];
    if (!meta) continue;
    for (const e of meta.entidades) {
      for (const r of e.referenciasExternas) {
        const clave = `${CLAVE_CONTEXTO[servicio]}>${r.contexto}`;
        const arista = aristas.get(clave) ?? { desde: CLAVE_CONTEXTO[servicio], hasta: r.contexto, campos: new Set() };
        arista.campos.add(`${e.nombre}.${r.campo}`);
        aristas.set(clave, arista);
      }
    }
  }

  const haciaIdentity = [...aristas.values()].filter((a) => a.hasta === 'Identity');
  haciaIdentity.forEach((a, i) => {
    const origen = rect(a.desde);
    const destino = rect('Identity');
    if (!origen || !destino) return;
    const reparto = (i + 1) / (haciaIdentity.length + 1);
    const xDestino = destino.izq + (destino.der - destino.izq) * reparto;
    const yMedio = (origen.arriba + destino.abajo) / 2;
    curva(svg, `M ${origen.x} ${origen.arriba} C ${origen.x} ${yMedio}, ${xDestino} ${yMedio}, ${xDestino} ${destino.abajo + 2}`, a, (origen.x + xDestino) / 2, yMedio);
  });

  for (const a of [...aristas.values()].filter((x) => x.hasta !== 'Identity')) {
    const origen = rect(a.desde);
    const destino = rect(a.hasta);
    if (!origen || !destino) continue;
    const hacia = origen.x > destino.x ? { x1: origen.izq, x2: destino.der + 2 } : { x1: origen.der, x2: destino.izq - 2 };
    const y = Math.min(origen.arriba, destino.arriba) + 44;
    curva(svg, `M ${hacia.x1} ${y} L ${hacia.x2} ${y}`, a, (hacia.x1 + hacia.x2) / 2, y);
  }
}

function marcador() {
  const defs = document.createElementNS(SVG_NS, 'defs');
  defs.innerHTML = '<marker id="flecha" markerWidth="10" markerHeight="8" refX="9" refY="4" orient="auto"><path d="M0 0L10 4L0 8z" fill="#B27A17"/></marker>';
  return defs;
}

function curva(svg, d, arista, xEtiqueta, yEtiqueta) {
  const camino = document.createElementNS(SVG_NS, 'path');
  camino.setAttribute('d', d);
  camino.setAttribute('class', 'edge');
  camino.setAttribute('marker-end', 'url(#flecha)');
  svg.append(camino);

  const texto = `${arista.campos.size} ${arista.campos.size === 1 ? 'ID' : 'IDs'}`;
  const ancho = texto.length * 6.4 + 16;
  const grupo = document.createElementNS(SVG_NS, 'g');
  const fondo = document.createElementNS(SVG_NS, 'rect');
  fondo.setAttribute('class', 'edge-label-bg');
  fondo.setAttribute('x', xEtiqueta - ancho / 2);
  fondo.setAttribute('y', yEtiqueta - 10);
  fondo.setAttribute('width', ancho);
  fondo.setAttribute('height', 20);
  fondo.setAttribute('rx', 10);
  const rotulo = document.createElementNS(SVG_NS, 'text');
  rotulo.setAttribute('class', 'edge-label');
  rotulo.setAttribute('x', xEtiqueta);
  rotulo.setAttribute('y', yEtiqueta + 4);
  rotulo.setAttribute('text-anchor', 'middle');
  rotulo.textContent = texto;
  const titulo = document.createElementNS(SVG_NS, 'title');
  titulo.textContent = [...arista.campos].join(', ');
  grupo.append(titulo, fondo, rotulo);
  svg.append(grupo);
}
