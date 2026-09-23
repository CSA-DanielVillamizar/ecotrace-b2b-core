// Punto de entrada: navegacion por hash, barra lateral, estado de los servicios y ciclo de dibujo.

import { iniciarApi, nombreServicio, servicios, urlBase } from './api.js';
import { esqueletoDeVista } from './components.js';
import { h, icono, vaciar } from './dom.js';
import { alCambiar, alCambiarSalud, comprobarSalud, estado, refrescar } from './store.js';
import { cargas } from './views/cargas.js';
import { flota } from './views/flota.js';
import { mapa } from './views/mapa.js';
import { organizaciones } from './views/organizaciones.js';
import { pagos } from './views/pagos.js';
import { resumen } from './views/resumen.js';

const RUTAS = [
  { ruta: 'resumen', titulo: 'Resumen operativo', nav: 'Resumen', icono: 'dashboard', vista: resumen, grupo: 'Operación',
    lede: 'Estado del ciclo completo: cargas, flota y fondos en Escrow, reunidos desde los cuatro servicios.' },
  { ruta: 'cargas', titulo: 'Cargas y seguimiento', nav: 'Cargas', icono: 'paquete', vista: cargas, grupo: 'Operación',
    contexto: 'cargo', contador: () => estado.cargas.length,
    lede: 'Cada carga involucra a un generador y a un transportista y avanza por una máquina de estados.' },
  { ruta: 'flota', titulo: 'Flota', nav: 'Flota', icono: 'camion', vista: flota, grupo: 'Operación',
    contexto: 'fleet', contador: () => estado.vehiculos.length + estado.conductores.length,
    lede: 'Vehículos y conductores de cada transportista.' },
  { ruta: 'pagos', titulo: 'Facturación y Escrow', nav: 'Facturación y Escrow', icono: 'recibo', vista: pagos, grupo: 'Operación',
    contexto: 'billing', contador: () => estado.pagos.length,
    lede: 'Los fondos quedan en custodia hasta que la carga se entrega y luego se liberan o se reembolsan.' },
  { ruta: 'organizaciones', titulo: 'Organizaciones y usuarios', nav: 'Organizaciones', icono: 'edificio', vista: organizaciones, grupo: 'Identidad',
    contexto: 'identity', contador: () => estado.tenants.length,
    lede: 'Las dos caras del marketplace y las personas que trabajan en cada una.' },
  { ruta: 'mapa', titulo: 'Mapa de contextos', nav: 'Mapa de contextos', icono: 'mapa', vista: mapa, grupo: 'Arquitectura',
    lede: 'Los cuatro Bounded Contexts, su base de datos propia y las referencias por ID que los conectan.',
    sinListas: true }
];

const enlacesNav = new Map();
let contenedorVista;
let contenedorSalud;
let limpiarVista;

function analizarHash() {
  const [ruta = 'resumen', id] = location.hash.replace(/^#\/?/, '').split('/');
  return { definicion: RUTAS.find((r) => r.ruta === ruta) ?? RUTAS[0], id };
}

// ---------- Estructura fija ----------

function construirNavegacion() {
  const nav = document.getElementById('navegacion');
  let grupoActual;
  for (const definicion of RUTAS) {
    if (definicion.grupo !== grupoActual) {
      grupoActual = definicion.grupo;
      nav.append(h('div', { class: 'nav-title' }, grupoActual));
    }
    const contador = h('span', { class: 'count' });
    const enlace = h('a', { href: `#/${definicion.ruta}` }, icono(definicion.icono), definicion.nav, contador);
    enlacesNav.set(definicion.ruta, { enlace, contador, definicion });
    nav.append(enlace);
  }

  const docs = document.getElementById('enlaces-docs');
  for (const servicio of servicios()) {
    docs.append(h('a', { href: `${urlBase(servicio)}/swagger`, target: '_blank', rel: 'noopener' }, nombreServicio(servicio).split(' ')[0]));
  }
}

function actualizarNavegacion(rutaActiva) {
  for (const [ruta, { enlace, contador, definicion }] of enlacesNav) {
    if (ruta === rutaActiva) enlace.setAttribute('aria-current', 'page');
    else enlace.removeAttribute('aria-current');
    contador.textContent = definicion.contador?.() || '';
  }
}

// ---------- Salud de los servicios ----------

function pintarSalud() {
  const pildoras = servicios().map((servicio) => {
    const s = estado.salud[servicio];
    const detalle = s == null ? 'comprobando…' : s.ok ? `${s.ms} ms` : 'sin conexión';
    return h('li', { class: 'pill', dataset: { estado: s == null ? 'pendiente' : s.ok ? 'ok' : 'caido' } },
      h('span', { class: 'dot' }), nombreServicio(servicio), h('small', {}, detalle));
  });

  const caidos = servicios().filter((s) => estado.salud[s] && !estado.salud[s].ok);
  const aviso = caidos.length === 0 ? null : h('div', { class: 'banner', role: 'alert', dataset: { tone: 'bad' } },
    icono('alerta'),
    h('div', {},
      h('strong', {}, `No se puede conectar con ${caidos.map(nombreServicio).join(', ')}`),
      'Las demás pantallas siguen funcionando con los servicios disponibles. Para levantar todo, ejecute ',
      h('code', {}, 'scripts/run-all.ps1'), ' (Windows) o ', h('code', {}, 'scripts/run-all.sh'), '.'));

  // replaceChildren convierte null en el texto "null": hay que descartar lo que no existe.
  contenedorSalud.replaceChildren(
    ...[h('ul', { class: 'health', 'aria-label': 'Estado de los servicios' }, pildoras), aviso].filter(Boolean));
}

// ---------- Dibujo de la vista actual ----------

let ultimaRuta = '';

function pintar({ moverFoco = false } = {}) {
  limpiarVista?.();
  limpiarVista = undefined;

  const { definicion, id } = analizarHash();
  // Mientras llegan las listas no se dibuja la vista: una tabla vacia diria que no hay datos.
  const resultado = !estado.cargado && !definicion.sinListas
    ? { contenido: esqueletoDeVista() }
    : definicion.vista({ id });
  const titulo = h('h1', { tabindex: '-1' }, definicion.titulo);

  const contexto = definicion.contexto
    ? h('p', { class: 'muted', style: 'margin-top:6px;font-size:0.8125rem' }, `Contexto: ${nombreServicio(definicion.contexto)}`)
    : null;

  vaciar(contenedorVista).append(
    h('header', { class: 'page-head' },
      h('div', {}, titulo, contexto, h('p', { class: 'lede' }, definicion.lede)),
      h('div', { class: 'page-actions' }, resultado.acciones ?? [])),
    resultado.contenido);

  document.title = `${definicion.titulo} · EcoTrace Console`;
  actualizarNavegacion(definicion.ruta);
  limpiarVista = resultado.alMontar?.(contenedorVista);

  if (moverFoco && definicion.ruta !== ultimaRuta) titulo.focus({ preventScroll: true });
  ultimaRuta = definicion.ruta;
}

// ---------- Arranque ----------

async function arrancar() {
  contenedorVista = document.getElementById('vista');
  contenedorSalud = document.getElementById('salud');

  try {
    await iniciarApi();
  } catch {
    contenedorVista.replaceChildren(h('div', { class: 'banner', role: 'alert', dataset: { tone: 'bad' } },
      icono('alerta'), h('div', {}, h('strong', {}, 'No se pudo leer la configuración'), 'La consola necesita /config.json para saber dónde están las APIs.')));
    return;
  }

  construirNavegacion();
  alCambiarSalud(pintarSalud);
  alCambiar(() => pintar());
  window.addEventListener('hashchange', () => pintar({ moverFoco: true }));

  pintarSalud();
  pintar();
  await Promise.all([comprobarSalud(), refrescar()]);
  setInterval(comprobarSalud, 15000);
}

arrancar();
