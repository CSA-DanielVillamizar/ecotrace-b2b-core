import { api } from '../api.js';
import { boton, bloque, estadoVacio, formularioModal, idChip, insignia, insigniaTenant, tabla } from '../components.js';
import { fecha, h } from '../dom.js';
import { estado, nombreTenant, refrescar } from '../store.js';

const ROLES_POR_DEFECTO = ['Conductor', 'Supervisor', 'Administrador', 'Auditor'];

export function organizaciones() {
  const acciones = [
    boton({ texto: 'Nueva organización', icono: 'mas', onClick: nuevaOrganizacion }),
    boton({ texto: 'Nuevo usuario', icono: 'usuarios', variante: 'secondary', onClick: nuevoUsuario })
  ];

  if (estado.cargado && estado.tenants.length === 0 && !estado.errores.identity) {
    return {
      acciones,
      contenido: estadoVacio({
        icono: 'edificio',
        titulo: 'Aún no hay organizaciones',
        texto: 'EcoTrace es un marketplace de dos lados. Cree al menos una organización Generadora (quien produce la carga y paga) y una Transportista (quien la mueve y cobra).',
        accion: boton({ texto: 'Nueva organización', icono: 'mas', onClick: nuevaOrganizacion })
      })
    };
  }

  const usuariosDe = (tenantId) => estado.users.filter((u) => u.tenantId === tenantId).length;

  return {
    acciones,
    contenido: h('div', { class: 'stack-lg' },
      bloque('Organizaciones', 'Cada organización pertenece a un lado del marketplace. Los demás contextos guardan su identificador y nada más.',
        tabla({
          descripcion: 'Organizaciones registradas en Identity',
          filas: estado.tenants,
          columnas: [
            { titulo: 'Nombre', celda: (t) => h('span', { class: 'cell-title' }, t.nombre) },
            { titulo: 'Tipo', celda: (t) => insigniaTenant(t.tenantType) },
            { titulo: 'Usuarios', numerica: true, celda: (t) => usuariosDe(t.tenantId) },
            { titulo: 'Creada', celda: (t) => fecha(t.creadoEn) },
            { titulo: 'Identificador', celda: (t) => idChip(t.tenantId) }
          ]
        })),

      bloque('Usuarios', 'Cada usuario pertenece a exactamente una organización y tiene un rol.',
        tabla({
          descripcion: 'Usuarios registrados en Identity',
          filas: estado.users,
          vacio: 'Aún no hay usuarios.',
          columnas: [
            { titulo: 'Nombre', celda: (u) => h('div', {}, h('span', { class: 'cell-title' }, u.nombre), h('span', { class: 'sub' }, u.email)) },
            { titulo: 'Rol', celda: (u) => insignia(u.role, 'neutral') },
            { titulo: 'Organización', celda: (u) => nombreTenant(u.tenantId) },
            { titulo: 'Identificador', celda: (u) => idChip(u.userId) }
          ]
        })),

      bloque('Roles y permisos', 'Los permisos viajarán como claims en el JWT que Identity emite en el Trabajo 3.',
        h('div', { class: 'grid-2' }, estado.roles.map((rol) =>
          h('div', { class: 'card card-pad' },
            h('h3', {}, rol.nombre),
            h('ul', { class: 'refs' }, rol.permisos.map((p) => h('li', { class: 'ref' }, h('code', {}, p))))))))
    )
  };
}

function nuevaOrganizacion() {
  return formularioModal({
    titulo: 'Nueva organización',
    subtitulo: 'Se crea en Identity, el único servicio que conoce a las organizaciones.',
    textoEnvio: 'Crear organización',
    mensajeOk: (t) => `Organización «${t.nombre}» creada.`,
    campos: [
      { nombre: 'nombre', etiqueta: 'Nombre', requerido: true, marcador: 'Ej.: Transportes del Cauca' },
      {
        nombre: 'tenantType', etiqueta: 'Tipo', tipo: 'select', requerido: true,
        ayuda: 'Generador produce la carga y paga. Transportista la mueve y cobra.',
        opciones: [{ valor: 'Generador', texto: 'Generador' }, { valor: 'Transportista', texto: 'Transportista' }]
      }
    ],
    enviar: async (datos) => {
      const tenant = await api.identity.crearTenant(datos);
      await refrescar();
      return tenant;
    }
  });
}

function nuevoUsuario() {
  const roles = (estado.roles.length ? estado.roles.map((r) => r.nombre) : ROLES_POR_DEFECTO)
    .map((nombre) => ({ valor: nombre, texto: nombre }));

  return formularioModal({
    titulo: 'Nuevo usuario',
    subtitulo: 'El usuario queda dentro de la organización elegida.',
    textoEnvio: 'Crear usuario',
    mensajeOk: (u) => `Usuario «${u.nombre}» creado.`,
    campos: [
      {
        nombre: 'tenantId', etiqueta: 'Organización', tipo: 'select', requerido: true,
        sinOpciones: 'Cree primero una organización.',
        opciones: estado.tenants.map((t) => ({ valor: t.tenantId, texto: `${t.nombre} (${t.tenantType})` }))
      },
      { nombre: 'role', etiqueta: 'Rol', tipo: 'select', requerido: true, opciones: roles },
      { nombre: 'nombre', etiqueta: 'Nombre completo', requerido: true, mitad: true },
      { nombre: 'email', etiqueta: 'Correo electrónico', tipo: 'email', requerido: true, mitad: true, marcador: 'nombre@empresa.co' }
    ],
    enviar: async (datos) => {
      const usuario = await api.identity.crearUser(datos);
      await refrescar();
      return usuario;
    }
  });
}
