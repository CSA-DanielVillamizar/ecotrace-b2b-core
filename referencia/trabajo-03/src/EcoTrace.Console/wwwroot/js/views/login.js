// Pantalla de inicio de sesión. Mientras no haya sesión, la consola no pide datos a ningún servicio: todos exigen un token.

import { ApiError } from '../api.js';
import { iniciarSesion } from '../auth.js';
import { conCarga } from '../components.js';
import { h, icono } from '../dom.js';

export function pantallaDeInicioDeSesion({ alIniciar }) {
  const cajaError = h('div', { class: 'form-error', role: 'alert', hidden: true });
  const correo = h('input', {
    class: 'input', id: 'login-correo', type: 'email', autocomplete: 'username', required: true,
    placeholder: 'admin@ecotrace.local'
  });
  const clave = h('input', { class: 'input', id: 'login-clave', type: 'password', autocomplete: 'current-password', required: true });
  const enviar = h('button', { class: 'btn', type: 'submit' }, 'Iniciar sesión');

  const formulario = h('form', { class: 'form', novalidate: true, 'aria-labelledby': 'login-titulo' },
    cajaError,
    h('div', { class: 'field' }, h('label', { for: 'login-correo' }, 'Correo electrónico'), correo),
    h('div', { class: 'field' }, h('label', { for: 'login-clave' }, 'Contraseña'), clave),
    h('div', {}, enviar));

  formulario.addEventListener('submit', async (evento) => {
    evento.preventDefault();
    cajaError.hidden = true;

    if (!correo.value.trim() || !clave.value) {
      cajaError.textContent = 'Escriba el correo y la contraseña.';
      cajaError.hidden = false;
      return;
    }

    await conCarga(enviar, async () => {
      try {
        await iniciarSesion(correo.value.trim(), clave.value);
        clave.value = '';
        alIniciar();
      } catch (error) {
        cajaError.textContent = error instanceof ApiError
          ? (error.estado === 401 ? 'Credenciales inválidas.' : error.message)
          : `Error inesperado: ${error.message}`;
        cajaError.hidden = false;
      }
    });
  });

  return {
    contenido: h('section', { class: 'block', style: 'max-width: 28rem' },
      h('div', { class: 'block-title' }, icono('info'), h('h3', { id: 'login-titulo' }, 'Inicie sesión para ver la plataforma')),
      h('p', { class: 'muted', style: 'margin-bottom: 16px' },
        'Cada servicio exige un token que emite Identity. En desarrollo, el administrador de la plataforma es ',
        h('code', {}, 'admin@ecotrace.local'), '; su contraseña está en ', h('code', {}, 'appsettings.Development.json'), ' de Identity.'),
      formulario)
  };
}
