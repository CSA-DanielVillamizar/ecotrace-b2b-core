import { api } from '../api.js';
import { boton, bloque, estadoVacio, formularioModal, idChip, tabla } from '../components.js';
import { fecha, h, numero } from '../dom.js';
import { estado, nombreTenant, nombreUsuario, refrescar } from '../store.js';

const transportistas = () => estado.tenants.filter((t) => t.tenantType === 'Transportista');
const usuariosDe = (tenantId) => estado.users.filter((u) => u.tenantId === tenantId);

export function flota() {
  const acciones = [
    boton({ texto: 'Registrar vehículo', icono: 'mas', onClick: registrarVehiculo }),
    boton({ texto: 'Registrar conductor', icono: 'usuarios', variante: 'secondary', onClick: registrarConductor })
  ];

  if (estado.cargado && !estado.errores.fleet && estado.vehiculos.length === 0 && estado.conductores.length === 0) {
    return {
      acciones,
      contenido: estadoVacio({
        icono: 'camion',
        titulo: 'Aún no hay flota registrada',
        texto: 'Registre los vehículos y conductores de un transportista. Fleet Management guarda el identificador de la organización y del usuario que registra, sin consultar a Identity.',
        accion: boton({ texto: 'Registrar vehículo', icono: 'mas', onClick: registrarVehiculo })
      })
    };
  }

  return {
    acciones,
    contenido: h('div', { class: 'stack-lg' },
      bloque('Vehículos', 'La placa es única en toda la plataforma.',
        tabla({
          descripcion: 'Vehículos registrados en Fleet Management',
          filas: estado.vehiculos,
          columnas: [
            { titulo: 'Placa', celda: (v) => h('span', { class: 'cell-title mono' }, v.placa) },
            { titulo: 'Capacidad', numerica: true, celda: (v) => `${numero(v.capacidadKg)} kg` },
            { titulo: 'Transportista', celda: (v) => nombreTenant(v.tenantId) },
            { titulo: 'Registrado por', celda: (v) => nombreUsuario(v.registradoPorUserId) },
            { titulo: 'Alta', celda: (v) => fecha(v.creadoEn) },
            { titulo: 'Identificador', celda: (v) => idChip(v.vehiculoId) }
          ]
        })),

      bloque('Conductores', 'La licencia es única dentro de cada transportista.',
        tabla({
          descripcion: 'Conductores registrados en Fleet Management',
          filas: estado.conductores,
          vacio: 'Aún no hay conductores.',
          columnas: [
            { titulo: 'Nombre', celda: (c) => h('span', { class: 'cell-title' }, c.nombre) },
            { titulo: 'Licencia', celda: (c) => h('span', { class: 'mono' }, c.licencia) },
            { titulo: 'Transportista', celda: (c) => nombreTenant(c.tenantId) },
            { titulo: 'Cuenta de usuario', celda: (c) => (c.userId ? nombreUsuario(c.userId) : h('span', { class: 'muted' }, 'Sin cuenta')) },
            { titulo: 'Identificador', celda: (c) => idChip(c.conductorId) }
          ]
        })))
  };
}

const camposDeTransportista = () => [
  {
    nombre: 'tenantId', etiqueta: 'Transportista', tipo: 'select', requerido: true,
    sinOpciones: 'Cree primero una organización de tipo Transportista.',
    opciones: transportistas().map((t) => ({ valor: t.tenantId, texto: t.nombre }))
  },
  {
    nombre: 'registradoPorUserId', etiqueta: 'Registrado por', tipo: 'select', requerido: true,
    ayuda: 'Usuario de Identity que realiza el alta. Fleet Management guarda solo su identificador.',
    sinOpciones: 'Elija un transportista con usuarios.',
    opciones: (v) => usuariosDe(v.tenantId).map((u) => ({ valor: u.userId, texto: `${u.nombre} (${u.role})` }))
  }
];

function registrarVehiculo() {
  return formularioModal({
    titulo: 'Registrar vehículo',
    subtitulo: 'Se guarda en Fleet Management con el identificador del transportista.',
    textoEnvio: 'Registrar vehículo',
    mensajeOk: (v) => `Vehículo ${v.placa} registrado.`,
    campos: [
      ...camposDeTransportista(),
      { nombre: 'placa', etiqueta: 'Placa', requerido: true, mitad: true, marcador: 'ABC123' },
      { nombre: 'capacidadKg', etiqueta: 'Capacidad (kg)', tipo: 'numero', requerido: true, mitad: true, min: 1, max: 80000 }
    ],
    enviar: async (datos) => {
      const vehiculo = await api.fleet.crearVehiculo(datos);
      await refrescar();
      return vehiculo;
    }
  });
}

function registrarConductor() {
  return formularioModal({
    titulo: 'Registrar conductor',
    subtitulo: 'Se guarda en Fleet Management con el identificador del transportista.',
    textoEnvio: 'Registrar conductor',
    mensajeOk: (c) => `Conductor «${c.nombre}» registrado.`,
    campos: [
      ...camposDeTransportista(),
      { nombre: 'nombre', etiqueta: 'Nombre completo', requerido: true, mitad: true },
      { nombre: 'licencia', etiqueta: 'Licencia', requerido: true, mitad: true, marcador: 'C2-123456' },
      {
        nombre: 'userId', etiqueta: 'Cuenta de usuario del conductor (opcional)', tipo: 'select',
        ayuda: 'Si el conductor ya tiene usuario en Identity, vincúlelo por su identificador.',
        sinOpciones: 'Este transportista no tiene usuarios con rol Conductor.',
        opciones: (v) => usuariosDe(v.tenantId).filter((u) => u.role === 'Conductor').map((u) => ({ valor: u.userId, texto: u.nombre }))
      }
    ],
    enviar: async (datos) => {
      const conductor = await api.fleet.crearConductor({ ...datos, userId: datos.userId || null });
      await refrescar();
      return conductor;
    }
  });
}
