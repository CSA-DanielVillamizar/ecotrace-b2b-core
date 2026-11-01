// Utilidades de DOM y formato. Todo el contenido dinamico se crea con nodos de texto,
// nunca con innerHTML, asi ningun dato que llegue de una API puede inyectar marcado.

const SVG_NS = 'http://www.w3.org/2000/svg';

/** Crea un elemento. `props.class`, `props.dataset` y `onXxx` tienen tratamiento especial. */
export function h(etiqueta, props = {}, ...hijos) {
  const el = document.createElement(etiqueta);
  for (const [clave, valor] of Object.entries(props ?? {})) {
    if (valor == null || valor === false) continue;
    if (clave === 'class') el.className = valor;
    else if (clave === 'dataset') Object.assign(el.dataset, valor);
    else if (clave.startsWith('on') && typeof valor === 'function') el.addEventListener(clave.slice(2).toLowerCase(), valor);
    else if (valor === true) el.setAttribute(clave, '');
    else el.setAttribute(clave, valor);
  }
  agregar(el, hijos);
  return el;
}

export function agregar(el, hijos) {
  for (const hijo of hijos.flat(Infinity)) {
    if (hijo == null || hijo === false) continue;
    el.append(hijo instanceof Node ? hijo : document.createTextNode(String(hijo)));
  }
  return el;
}

export function vaciar(el) {
  while (el.firstChild) el.removeChild(el.firstChild);
  return el;
}

// Iconos de trazo, 24x24. Son constantes de este archivo (no vienen de ninguna API).
const ICONOS = {
  dashboard: '<rect x="3" y="3" width="7" height="9" rx="1.5"/><rect x="14" y="3" width="7" height="5" rx="1.5"/><rect x="14" y="12" width="7" height="9" rx="1.5"/><rect x="3" y="16" width="7" height="5" rx="1.5"/>',
  mapa: '<circle cx="6" cy="6" r="2.5"/><circle cx="18" cy="6" r="2.5"/><circle cx="12" cy="18" r="2.5"/><path d="M8.3 7.3l2.4 8.4M15.7 7.3l-2.4 8.4M8.6 6h6.8"/>',
  edificio: '<path d="M4 21V5.5A1.5 1.5 0 0 1 5.5 4h8A1.5 1.5 0 0 1 15 5.5V21"/><path d="M15 10h3.5a1.5 1.5 0 0 1 1.5 1.5V21"/><path d="M3 21h18M8 8h3M8 12h3M8 16h3"/>',
  camion: '<path d="M3 6.5A1.5 1.5 0 0 1 4.5 5H14v11H3z"/><path d="M14 9h4l3 3.5V16h-7"/><circle cx="7.5" cy="17.5" r="2"/><circle cx="17.5" cy="17.5" r="2"/>',
  paquete: '<path d="M12 3l8 4.5v9L12 21l-8-4.5v-9z"/><path d="M4 7.5l8 4.5 8-4.5M12 12v9"/>',
  recibo: '<path d="M6 3h12v18l-3-2-3 2-3-2-3 2z"/><path d="M9 8h6M9 12h6"/>',
  usuarios: '<circle cx="9" cy="8" r="3.2"/><path d="M3 20c0-3.3 2.7-6 6-6s6 2.7 6 6"/><path d="M16 5.2a3.2 3.2 0 0 1 0 5.6M18 14.4c1.8.8 3 2.7 3 5.6"/>',
  mas: '<path d="M12 5v14M5 12h14"/>',
  check: '<path d="M5 12.5l4.5 4.5L19 7.5"/>',
  alerta: '<path d="M12 4L2.8 19.5h18.4z"/><path d="M12 10v4.5M12 17v.01"/>',
  info: '<circle cx="12" cy="12" r="9"/><path d="M12 11v5M12 8v.01"/>',
  x: '<path d="M6 6l12 12M18 6L6 18"/>',
  copiar: '<rect x="8" y="8" width="12" height="12" rx="2"/><path d="M16 8V6a2 2 0 0 0-2-2H6a2 2 0 0 0-2 2v8a2 2 0 0 0 2 2h2"/>',
  refrescar: '<path d="M20 11a8 8 0 0 0-14.5-4M4 4v4h4"/><path d="M4 13a8 8 0 0 0 14.5 4M20 20v-4h-4"/>',
  base: '<ellipse cx="12" cy="5.5" rx="7.5" ry="2.8"/><path d="M4.5 5.5V12c0 1.5 3.4 2.8 7.5 2.8s7.5-1.3 7.5-2.8V5.5M4.5 12v6c0 1.5 3.4 2.8 7.5 2.8s7.5-1.3 7.5-2.8v-6"/>',
  externo: '<path d="M14 4h6v6M20 4l-9 9M18 14v4a2 2 0 0 1-2 2H6a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2h4"/>',
  enlace: '<path d="M10 14a4 4 0 0 0 5.7 0l3-3a4 4 0 0 0-5.7-5.7l-1 1"/><path d="M14 10a4 4 0 0 0-5.7 0l-3 3A4 4 0 0 0 11 18.7l1-1"/>',
  play: '<path d="M8 5v14l11-7z"/>',
  escudo: '<path d="M12 3l7.5 3v5.5c0 4.5-3.2 8-7.5 9.5-4.3-1.5-7.5-5-7.5-9.5V6z"/><path d="M9 12l2.2 2.2L15.5 10"/>'
};

export function icono(nombre, clase = 'icon') {
  const svg = document.createElementNS(SVG_NS, 'svg');
  svg.setAttribute('viewBox', '0 0 24 24');
  svg.setAttribute('class', clase);
  svg.setAttribute('aria-hidden', 'true');
  svg.setAttribute('focusable', 'false');
  svg.innerHTML = ICONOS[nombre] ?? '';
  return svg;
}

// ---------- Formato ----------

const formatoMoneda = new Intl.NumberFormat('es-CO', { style: 'currency', currency: 'COP', maximumFractionDigits: 0 });
const formatoNumero = new Intl.NumberFormat('es-CO');
const formatoFecha = new Intl.DateTimeFormat('es-CO', { dateStyle: 'medium', timeStyle: 'short' });

export const moneda = (n) => formatoMoneda.format(n);
export const numero = (n) => formatoNumero.format(n);
export const fecha = (iso) => (iso ? formatoFecha.format(new Date(iso)) : '');
export const idCorto = (id) => (id ?? '').slice(0, 8);
export const plural = (n, uno, varios) => `${numero(n)} ${n === 1 ? uno : varios}`;

// ---------- Avisos ----------

let regionAvisos;

// Los dialogos modales viven en la capa superior del navegador y taparian cualquier aviso
// normal. La region de avisos es un popover, que tambien entra en esa capa: volver a mostrarla
// cada vez la sube por encima del ultimo dialogo abierto.
function mostrarRegionDeAvisos() {
  regionAvisos ??= document.getElementById('avisos');
  if (typeof regionAvisos.showPopover !== 'function') return;
  if (regionAvisos.matches(':popover-open')) regionAvisos.hidePopover();
  regionAvisos.showPopover();
}

export function aviso(mensaje, tono = 'info', ms = tono === 'bad' ? 8000 : 4500) {
  const cierra = () => {
    el.remove();
    if (!regionAvisos.children.length && regionAvisos.matches(':popover-open')) regionAvisos.hidePopover();
  };
  const el = h('div', { class: 'toast', dataset: { tone: tono }, role: tono === 'bad' ? 'alert' : 'status' },
    icono(tono === 'ok' ? 'check' : tono === 'bad' ? 'alerta' : 'info'),
    h('div', {}, mensaje),
    h('button', { type: 'button', 'aria-label': 'Cerrar aviso', onClick: cierra }, icono('x')));
  regionAvisos ??= document.getElementById('avisos');
  regionAvisos.append(el);
  mostrarRegionDeAvisos();
  setTimeout(cierra, ms);
}

export async function copiar(texto) {
  try {
    await navigator.clipboard.writeText(texto);
    aviso('Identificador copiado.', 'ok', 2500);
  } catch {
    aviso('No se pudo copiar. Seleccione el identificador y use Ctrl+C.', 'bad');
  }
}
