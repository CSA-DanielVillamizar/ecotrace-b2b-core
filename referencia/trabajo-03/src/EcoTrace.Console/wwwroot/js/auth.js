// Sesión de la consola (ADR 0004). La consola es un cliente más: inicia sesión en Identity con un correo y una contraseña,
// guarda el access token y el refresh token, y manda el access token a cada servicio. Cuando el access token está por
// vencer lo cambia por uno nuevo con el refresh token; si eso falla, la sesión terminó y vuelve a pedir el inicio.
//
// Dónde se guardan: en sessionStorage (se borran al cerrar la pestaña), nunca en localStorage. Para una consola de
// referencia es un compromiso razonable; una aplicación web de producción usaría cookies HttpOnly emitidas por un BFF.

import { ApiError, api, conToken, configurarSesion } from './api.js';

const CLAVE = 'ecotrace.sesion';
const MARGEN_MS = 60_000;

let sesion = leerGuardada();
let renovando = null;
const oyentes = new Set();

function leerGuardada() {
  try {
    return JSON.parse(sessionStorage.getItem(CLAVE));
  } catch {
    return null;
  }
}

function guardar(nueva) {
  sesion = nueva;
  try {
    if (nueva) sessionStorage.setItem(CLAVE, JSON.stringify(nueva));
    else sessionStorage.removeItem(CLAVE);
  } catch {
    // Sin sessionStorage la sesión vive solo mientras la pestaña no se recargue.
  }
  oyentes.forEach((fn) => fn());
}

/** Los claims de un JWT, solo para mostrar quién es: la validación de verdad la hace cada servicio, con la firma. */
function leerClaims(token) {
  const carga = token.split('.')[1].replace(/-/g, '+').replace(/_/g, '/');
  return JSON.parse(new TextDecoder().decode(Uint8Array.from(atob(carga), (c) => c.charCodeAt(0))));
}

const desdeRespuesta = (email, respuesta) => {
  const claims = leerClaims(respuesta.accessToken);
  return {
    email,
    rol: claims.role,
    tenantId: claims.tenant_id,
    operador: claims.plataforma === 'true',
    accessToken: respuesta.accessToken,
    refreshToken: respuesta.refreshToken,
    expiraEn: new Date(respuesta.expiraEn).getTime()
  };
};

export const haySesion = () => sesion != null;
export const usuarioActual = () => sesion;

export function alCambiarSesion(fn) {
  oyentes.add(fn);
  return () => oyentes.delete(fn);
}

export async function iniciarSesion(email, password) {
  guardar(desdeRespuesta(email, await api.identity.login({ email, password })));
}

/** Inicia sesión como otra persona sin tocar la sesión actual. Sirve para los datos de ejemplo. */
export async function entrarComo(email, password) {
  return (await api.identity.login({ email, password })).accessToken;
}

export async function cerrarSesion() {
  const refresco = sesion?.refreshToken;
  guardar(null);
  if (refresco) {
    try {
      await api.identity.logout({ refreshToken: refresco });
    } catch {
      // Si Identity no responde, la sesión local ya terminó; el refresh token vence solo a los 7 días.
    }
  }
}

/** El access token vigente, renovado si hace falta, o null si no hay sesión. */
async function tokenVigente() {
  if (!sesion) return null;
  if (sesion.expiraEn - Date.now() > MARGEN_MS) return sesion.accessToken;

  // Varias llamadas a la vez comparten una sola renovación.
  renovando ??= (async () => {
    try {
      const respuesta = await api.identity.refresh({ refreshToken: sesion.refreshToken });
      guardar({ ...desdeRespuesta(sesion.email, respuesta), refreshToken: sesion.refreshToken });
    } catch {
      guardar(null);
    } finally {
      renovando = null;
    }
  })();
  await renovando;
  return sesion?.accessToken ?? null;
}

// ---------- Personas de demostración ----------
//
// Los servicios solo dejan escribir a la persona que corresponde: el supervisor del transportista asigna y registra, el
// administrador del generador paga, y el operador de la plataforma solo crea organizaciones y lee. La consola inicia sesión
// como el operador; para las demás acciones usa las cuentas que ELLA misma creó (desde «Organizaciones» o al cargar los datos
// de ejemplo), cuyas credenciales guarda en sessionStorage mientras dure la pestaña. Son usuarios de demostración, de usar y
// tirar: una aplicación real nunca guarda contraseñas en el navegador.

const CLAVE_PERSONAS = 'ecotrace.personas';

function leerPersonas() {
  try {
    return JSON.parse(sessionStorage.getItem(CLAVE_PERSONAS)) ?? [];
  } catch {
    return [];
  }
}

export function recordarPersona(persona) {
  try {
    const otras = leerPersonas().filter((p) => p.userId !== persona.userId);
    sessionStorage.setItem(CLAVE_PERSONAS, JSON.stringify([...otras, persona]));
  } catch {
    // Sin sessionStorage la consola no podrá actuar como esa persona después de recargar.
  }
}

/**
 * Hace las llamadas de `accion` como una persona de demostración: la del usuario dado, o una de las organizaciones dadas
 * con alguno de los roles dados. Si la consola no conoce a nadie así, lo dice con un mensaje que explica cómo arreglarlo.
 */
export async function comoPersona({ userId, tenantIds = [], roles = [] }, accion) {
  const personas = leerPersonas();
  const elegida = userId
    ? personas.find((p) => p.userId === userId)
    : personas.find((p) => tenantIds.includes(p.tenantId) && roles.includes(p.rol));

  if (!elegida) {
    throw new ApiError(403, {
      detail: 'Esta acción la hace una persona de la organización y la consola no tiene sus credenciales. ' +
        'Cree el usuario desde «Organizaciones» (con contraseña) o cargue los datos de ejemplo.'
    }, 'identity');
  }

  return conToken(await entrarComo(elegida.email, elegida.password), accion);
}

configurarSesion({ token: tokenVigente, alNoAutorizado: () => guardar(null) });
