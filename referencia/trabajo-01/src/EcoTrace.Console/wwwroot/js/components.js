// Componentes de interfaz compartidos por todas las vistas.

import { ApiError } from './api.js';
import { aviso, copiar, h, icono, idCorto } from './dom.js';

// ---------- Estados de negocio: texto, tono y color de grafico ----------
// El estado siempre se muestra con texto; el color solo refuerza.

export const ESTADOS_CARGA = {
  Pendiente: { texto: 'Pendiente', tono: 'neutral', color: 'var(--neutral-solid)' },
  Asignado: { texto: 'Asignada', tono: 'info', color: '#8DBBCC' },
  EnTransito: { texto: 'En tránsito', tono: 'info', color: 'var(--info-solid)' },
  ConNovedad: { texto: 'Con novedad', tono: 'warn', color: 'var(--warn-solid)' },
  Entregado: { texto: 'Entregada', tono: 'ok', color: 'var(--ok-solid)' }
};

export const ESTADOS_ESCROW = {
  EnCustodia: { texto: 'En custodia', tono: 'warn' },
  Liberado: { texto: 'Liberado', tono: 'ok' },
  Reembolsado: { texto: 'Reembolsado', tono: 'neutral' }
};

// Espejo de la maquina de estados del dominio, solo para ofrecer opciones validas en el formulario.
// La autoridad es el servidor: si esto se desincroniza, la API responde 409 con la razon.
export const SIGUIENTES_CARGA = {
  Asignado: ['EnTransito'],
  EnTransito: ['ConNovedad', 'Entregado'],
  ConNovedad: ['EnTransito', 'Entregado']
};

export const insignia = (texto, tono = 'neutral') => h('span', { class: 'badge', dataset: { tone: tono } }, texto);

export const insigniaCarga = (estado) => insignia(ESTADOS_CARGA[estado]?.texto ?? estado, ESTADOS_CARGA[estado]?.tono);
export const insigniaEscrow = (estado) => insignia(ESTADOS_ESCROW[estado]?.texto ?? estado, ESTADOS_ESCROW[estado]?.tono);
export const insigniaTenant = (tipo) => insignia(tipo, tipo === 'Generador' ? 'info' : 'neutral');

// ---------- Elementos basicos ----------

export function boton({ texto, icono: nombreIcono, variante = 'primary', pequeno = false, tipo = 'button', titulo, onClick, deshabilitado = false }) {
  const clases = ['btn', variante !== 'primary' && `btn-${variante}`, pequeno && 'btn-sm'].filter(Boolean).join(' ');
  return h('button', { class: clases, type: tipo, title: titulo, disabled: deshabilitado, onClick },
    nombreIcono && icono(nombreIcono), texto);
}

/** Ejecuta una accion asincrona mostrando el boton como ocupado y evitando doble clic. */
export async function conCarga(elBoton, accion) {
  if (elBoton.getAttribute('aria-busy') === 'true') return undefined;
  elBoton.setAttribute('aria-busy', 'true');
  try {
    return await accion();
  } finally {
    elBoton.removeAttribute('aria-busy');
  }
}

export function idChip(id) {
  return h('button', {
    type: 'button', class: 'id-chip', title: `${id} (clic para copiar)`,
    'aria-label': `Copiar identificador ${id}`,
    onClick: (evento) => { evento.stopPropagation(); copiar(id); }
  }, idCorto(id), icono('copiar'));
}

export function estadoVacio({ icono: nombreIcono = 'info', titulo, texto, accion }) {
  return h('div', { class: 'card empty' },
    h('div', { class: 'glyph' }, icono(nombreIcono)),
    h('h2', {}, titulo),
    texto && h('p', {}, texto),
    accion);
}

/** Marcador de carga: se muestra mientras llegan los datos, para no afirmar que algo esta vacio. */
export function esqueletoDeVista() {
  const bloque = (alto) => h('div', { class: 'skeleton', style: `height: ${alto}px` });
  return h('div', { class: 'stack-lg', 'aria-busy': 'true', 'aria-label': 'Cargando datos' },
    h('div', { class: 'kpis' }, [1, 2, 3, 4].map(() => h('div', { class: 'card kpi' }, bloque(88)))),
    bloque(220));
}

export function bloque(titulo, descripcion, ...contenido) {
  return h('section', {},
    h('div', { class: 'section-head' },
      h('div', {}, h('h2', {}, titulo), descripcion && h('p', {}, descripcion))),
    ...contenido);
}

// ---------- Tabla ----------

/**
 * columnas: [{ titulo, celda(fila) => Node|string, numerica?, clase? }]
 * Si hay `alAbrir`, cada fila es clicable y ademas trae un boton "Ver" accesible por teclado.
 */
export function tabla({ descripcion, columnas, filas, alAbrir, vacio = 'No hay registros todavía.' }) {
  if (!filas.length) return h('div', { class: 'table-wrap' }, h('p', { class: 'empty-inline' }, vacio));

  const encabezado = h('tr', {},
    columnas.map((c) => h('th', { scope: 'col', class: c.numerica ? 'num' : '' }, c.titulo)),
    alAbrir && h('th', { scope: 'col' }, h('span', { class: 'sr-only' }, 'Acciones')));

  const cuerpo = filas.map((fila) => {
    const tr = h('tr', { class: alAbrir ? 'clickable' : '', onClick: alAbrir ? () => alAbrir(fila) : null },
      columnas.map((c) => h('td', { class: [c.numerica && 'num', c.clase].filter(Boolean).join(' ') }, c.celda(fila))),
      alAbrir && h('td', {}, h('div', { class: 'row-actions' },
        boton({
          texto: 'Ver', variante: 'ghost', pequeno: true,
          onClick: (evento) => { evento.stopPropagation(); alAbrir(fila); }
        }))));
    return tr;
  });

  return h('div', { class: 'table-wrap' },
    h('table', {},
      h('caption', { class: 'sr-only' }, descripcion),
      h('thead', {}, encabezado),
      h('tbody', {}, cuerpo)));
}

// ---------- Paneles (dialogos nativos: cierran con Esc y atrapan el foco) ----------

let contadorPaneles = 0;

export function abrirPanel({ titulo, subtitulo, cuerpo, pie, tipo = 'drawer', alCerrar }) {
  const idTitulo = `panel-titulo-${++contadorPaneles}`;
  const dialogo = h('dialog', { class: tipo, 'aria-labelledby': idTitulo });
  const zonaCuerpo = h('div', { class: 'dlg-body' }, cuerpo);
  const zonaPie = h('div', { class: 'dlg-foot' }, pie);

  const cerrar = () => dialogo.close();
  dialogo.append(h('div', { class: 'dlg' },
    h('div', { class: 'dlg-head' },
      h('div', {}, h('h2', { id: idTitulo }, titulo), subtitulo && h('p', {}, subtitulo)),
      h('button', { type: 'button', class: 'btn btn-ghost btn-sm', 'aria-label': 'Cerrar', onClick: cerrar }, icono('x'))),
    zonaCuerpo,
    pie && zonaPie));

  dialogo.addEventListener('click', (evento) => { if (evento.target === dialogo) cerrar(); });
  dialogo.addEventListener('close', () => { dialogo.remove(); alCerrar?.(); });

  document.body.append(dialogo);
  dialogo.showModal();

  return {
    dialogo,
    cerrar,
    reemplazarCuerpo(...nodos) { zonaCuerpo.replaceChildren(...nodos.flat().filter(Boolean)); },
    reemplazarPie(...nodos) { zonaPie.replaceChildren(...nodos.flat().filter(Boolean)); }
  };
}

/** Modal de confirmacion. Resuelve true si la persona confirma. */
export function confirmar({ titulo, texto, textoConfirmar = 'Confirmar', variante = 'primary' }) {
  return new Promise((resolver) => {
    let decidido = false;
    const decidir = (valor) => { decidido = true; panel.cerrar(); resolver(valor); };
    const panel = abrirPanel({
      titulo, tipo: 'modal',
      cuerpo: h('p', {}, texto),
      pie: [
        boton({ texto: 'Cancelar', variante: 'secondary', onClick: () => decidir(false) }),
        boton({ texto: textoConfirmar, variante, onClick: () => decidir(true) })
      ],
      alCerrar: () => { if (!decidido) resolver(false); }
    });
  });
}

// ---------- Formularios ----------

/**
 * Abre un modal con un formulario y resuelve con el resultado de `enviar` (o undefined si se cancela).
 * campo: { nombre, etiqueta, tipo: texto|numero|email|select|area, requerido, ayuda, marcador, valor,
 *          min, max, opciones: [{valor,texto}] | (valores)=>[...], sinOpciones, mitad, alCambiar }
 */
export function formularioModal({ titulo, subtitulo, campos, textoEnvio, mensajeOk, enviar, tipo = 'modal' }) {
  return new Promise((resolver) => {
    const controles = new Map();
    const mensajes = new Map();
    let resultado;

    const cajaError = h('div', { class: 'form-error', role: 'alert', hidden: true });
    const form = h('form', { class: 'form', novalidate: true });
    const botonEnviar = h('button', { class: 'btn', type: 'submit' }, textoEnvio);

    const leer = () => Object.fromEntries(campos.map((c) => {
      const crudo = controles.get(c.nombre).value;
      if (c.tipo === 'numero') return [c.nombre, crudo === '' ? null : Number(crudo)];
      return [c.nombre, typeof crudo === 'string' ? crudo.trim() : crudo];
    }));

    const construirOpciones = (campo) => {
      const control = controles.get(campo.nombre);
      const previa = control.value;
      const lista = typeof campo.opciones === 'function' ? campo.opciones(leer()) : campo.opciones;
      control.replaceChildren(
        h('option', { value: '' }, lista.length ? 'Seleccione…' : (campo.sinOpciones ?? 'No hay opciones disponibles')),
        ...lista.map((o) => h('option', { value: o.valor }, o.texto)));
      control.disabled = lista.length === 0;
      control.value = lista.some((o) => o.valor === previa) ? previa : (campo.valor && lista.some((o) => o.valor === campo.valor) ? campo.valor : '');
    };

    const refrescarSelects = () => campos.filter((c) => c.tipo === 'select').forEach(construirOpciones);

    const filas = [];
    for (const campo of campos) {
      const idCampo = `campo-${campo.nombre}-${Math.random().toString(36).slice(2, 7)}`;
      const idAyuda = `${idCampo}-ayuda`;
      const idError = `${idCampo}-error`;
      let control;

      if (campo.tipo === 'select') {
        control = h('select', { class: 'input', id: idCampo });
      } else if (campo.tipo === 'area') {
        control = h('textarea', { class: 'input', id: idCampo, placeholder: campo.marcador });
      } else {
        control = h('input', {
          class: 'input', id: idCampo, placeholder: campo.marcador,
          type: campo.tipo === 'numero' ? 'number' : campo.tipo === 'email' ? 'email' : 'text',
          min: campo.min, max: campo.max, step: campo.paso, autocomplete: 'off', inputmode: campo.tipo === 'numero' ? 'decimal' : undefined
        });
      }

      control.setAttribute('aria-describedby', [campo.ayuda && idAyuda, idError].filter(Boolean).join(' '));
      if (campo.requerido) control.setAttribute('aria-required', 'true');
      if (campo.tipo !== 'select' && campo.valor != null) control.value = campo.valor;

      const mensaje = h('div', { class: 'err', id: idError, hidden: true });
      controles.set(campo.nombre, control);
      mensajes.set(campo.nombre, mensaje);

      control.addEventListener('change', () => { campo.alCambiar?.(leer()); refrescarSelects(); });

      const envoltura = h('div', { class: 'field' },
        h('label', { for: idCampo }, campo.etiqueta, campo.requerido ? ' *' : ''),
        control,
        campo.ayuda && h('div', { class: 'hint', id: idAyuda }, campo.ayuda),
        mensaje);
      filas.push({ campo, envoltura });
    }

    // Los campos marcados `mitad` se agrupan de dos en dos.
    const nodos = [];
    for (let i = 0; i < filas.length; i++) {
      if (filas[i].campo.mitad && filas[i + 1]?.campo.mitad) {
        nodos.push(h('div', { class: 'form-grid' }, filas[i].envoltura, filas[i + 1].envoltura));
        i++;
      } else {
        nodos.push(filas[i].envoltura);
      }
    }

    form.append(cajaError, ...nodos);
    refrescarSelects();

    const limpiarErrores = () => {
      cajaError.hidden = true;
      for (const [nombre, mensaje] of mensajes) {
        mensaje.hidden = true;
        controles.get(nombre).removeAttribute('aria-invalid');
      }
    };

    const marcar = (nombre, texto) => {
      const control = controles.get(nombre);
      const mensaje = mensajes.get(nombre);
      control.setAttribute('aria-invalid', 'true');
      mensaje.textContent = texto;
      mensaje.hidden = false;
    };

    form.addEventListener('submit', async (evento) => {
      evento.preventDefault();
      limpiarErrores();
      const datos = leer();

      const faltantes = campos.filter((c) => c.requerido && (datos[c.nombre] === '' || datos[c.nombre] == null));
      if (faltantes.length) {
        faltantes.forEach((c) => marcar(c.nombre, 'Este campo es obligatorio.'));
        controles.get(faltantes[0].nombre).focus();
        return;
      }

      botonEnviar.setAttribute('aria-busy', 'true');
      try {
        resultado = await enviar(datos);
        if (mensajeOk) aviso(typeof mensajeOk === 'function' ? mensajeOk(resultado) : mensajeOk, 'ok');
        panel.cerrar();
      } catch (error) {
        if (!(error instanceof ApiError)) throw error;
        const conError = campos.filter((c) => error.camposConError[c.nombre.toLowerCase()]);
        conError.forEach((c) => marcar(c.nombre, error.camposConError[c.nombre.toLowerCase()]));
        if (conError.length) {
          controles.get(conError[0].nombre).focus();
        } else {
          cajaError.textContent = error.message;
          cajaError.hidden = false;
        }
      } finally {
        botonEnviar.removeAttribute('aria-busy');
      }
    });

    const panel = abrirPanel({
      titulo, subtitulo, tipo,
      cuerpo: form,
      pie: [
        boton({ texto: 'Cancelar', variante: 'secondary', onClick: () => panel.cerrar() }),
        botonEnviar
      ],
      alCerrar: () => resolver(resultado)
    });

    // El boton de envio vive en el pie, fuera del <form>: se enlaza con el atributo `form`.
    form.id = `formulario-${++contadorPaneles}`;
    botonEnviar.setAttribute('form', form.id);

    (controles.get(campos[0]?.nombre)?.disabled ? botonEnviar : controles.get(campos[0].nombre)).focus();
  });
}
