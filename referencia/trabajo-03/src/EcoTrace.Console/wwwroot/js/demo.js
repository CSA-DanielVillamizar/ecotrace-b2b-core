// Conjunto de datos de ejemplo. Recorre el ciclo de negocio llamando a la API de cada servicio
// en orden y copiando a mano los identificadores de uno a otro. La entrega de la primera carga
// dispara el Saga de verdad: el evento sale por el Outbox y Billing libera el pago por su cuenta.

import { ApiError, api } from './api.js';
import { comoPersona, recordarPersona } from './auth.js';
import { abrirPanel, boton, confirmar } from './components.js';
import { aviso, h } from './dom.js';
import { estado, refrescar } from './store.js';

// Llamadas que hace `ejecutar`. Solo alimenta la barra de progreso: si cambia el guion, ajustelo.
const TOTAL_LLAMADAS = 37;

// Contraseña de las personas de ejemplo: usuarios de usar y tirar de un entorno de desarrollo.
const CLAVE_DE_DEMOSTRACION = 'Demostracion-2026-clave';

const azar = (largo, alfabeto) => Array.from({ length: largo }, () => alfabeto[Math.floor(Math.random() * alfabeto.length)]).join('');
const LETRAS = 'ABCDEFGHJKLMNPRSTUVWXYZ';
const DIGITOS = '0123456789';

export async function cargarDatosDeEjemplo() {
  // La organización que opera la plataforma siempre existe: no cuenta como datos de ejemplo.
  if (estado.tenants.some((t) => t.tenantType !== 'Plataforma')) {
    const seguir = await confirmar({
      titulo: 'Ya hay datos cargados',
      texto: 'Se agregará otro conjunto completo de ejemplo (organizaciones, flota, cargas y pagos) junto a lo que ya existe.',
      textoConfirmar: 'Agregar otro conjunto'
    });
    if (!seguir) return;
  }

  let hechas = 0;
  const barra = h('progress', { max: TOTAL_LLAMADAS, value: 0, style: 'width: 100%; height: 10px' });
  const texto = h('p', { role: 'status' }, 'Preparando…');

  // Cada llamada a una API pasa por aqui: muestra que se esta haciendo y avanza la barra.
  const paso = async (mensaje, llamada) => {
    texto.textContent = mensaje;
    const resultado = await llamada();
    barra.value = ++hechas;
    return resultado;
  };

  const panel = abrirPanel({
    titulo: 'Cargando datos de ejemplo',
    subtitulo: 'Cada dato se crea en la API del servicio que lo posee.',
    tipo: 'modal',
    cuerpo: h('div', { class: 'stack' }, barra, texto),
    pie: []
  });

  try {
    await ejecutar(paso);
    await refrescar();
    panel.cerrar();
    aviso('Datos de ejemplo cargados en los cuatro servicios.', 'ok');
  } catch (error) {
    await refrescar();
    const mensaje = error instanceof ApiError ? error.message : `Error inesperado: ${error.message}`;
    panel.reemplazarCuerpo(h('div', { class: 'form-error', role: 'alert' }, mensaje));
    panel.reemplazarPie(boton({ texto: 'Cerrar', variante: 'secondary', onClick: panel.cerrar }));
  }
}

async function ejecutar(paso) {
  const etiqueta = azar(4, 'abcdefghjkmnpqrstuvwxyz');
  const sinTildes = (texto) => texto.toLowerCase().normalize('NFD').replace(/[\u0300-\u036f]/g, '').replace(/\s+/g, '.');

  // 1. Identity emite los identificadores base. Lo hace el operador de la plataforma (la sesión de la consola): es el único
  // que crea organizaciones y usuarios de cualquier organización.
  const organizacion = (nombre, tenantType) =>
    paso('Identity: creando organizaciones…', () => api.identity.crearTenant({ nombre, tenantType }));
  const generadora = await organizacion('Generadora del Valle S.A.', 'Generador');
  const ecoindustrias = await organizacion('EcoIndustrias del Norte', 'Generador');
  const cauca = await organizacion('Transportes del Cauca', 'Transportista');
  const andina = await organizacion('Logística Andina', 'Transportista');

  // Cada persona nace con contraseña y la consola la recuerda en esta pestaña: así puede actuar como ella después.
  const usuario = async (tenant, role, nombre, dominio) => {
    const email = `${sinTildes(nombre)}.${etiqueta}@${dominio}`;
    const creado = await paso('Identity: creando usuarios…', () =>
      api.identity.crearUser({ tenantId: tenant.tenantId, role, nombre, email, password: CLAVE_DE_DEMOSTRACION }));
    recordarPersona({ userId: creado.userId, tenantId: tenant.tenantId, rol: role, email, password: CLAVE_DE_DEMOSTRACION });
    return creado;
  };
  const laura = await usuario(generadora, 'Administrador', 'Laura Mejía', 'generadoradelvalle.co');
  const andres = await usuario(ecoindustrias, 'Administrador', 'Andrés Pardo', 'ecoindustrias.co');
  const marta = await usuario(cauca, 'Supervisor', 'Marta Ríos', 'transportescauca.co');
  const julian = await usuario(andina, 'Supervisor', 'Julián Cárdenas', 'logisticaandina.co');
  const carlosUsuario = await usuario(cauca, 'Conductor', 'Carlos Ruiz', 'transportescauca.co');

  // Cada paso lo hace la persona que corresponde, con su propio token: el supervisor registra la flota y asigna, el
  // administrador del generador pide la carga y paga, y el conductor confirma la entrega. Los servicios no aceptan otra cosa.
  const como = (persona, mensaje, llamada) => paso(mensaje, () => comoPersona({ userId: persona.userId }, llamada));

  // 2. Fleet Management recibe solo los identificadores de Identity.
  const placa = () => `${azar(3, LETRAS)}${azar(3, DIGITOS)}`;
  const vehiculo = (tenant, registrador, capacidadKg) => como(registrador, 'Fleet Management: registrando vehículos…', () =>
    api.fleet.crearVehiculo({ tenantId: tenant.tenantId, registradoPorUserId: registrador.userId, placa: placa(), capacidadKg }));
  const camion1 = await vehiculo(cauca, marta, 8000);
  const camion2 = await vehiculo(cauca, marta, 5000);
  const camion3 = await vehiculo(andina, julian, 12000);
  const camion4 = await vehiculo(cauca, marta, 6000);

  const conductor = (tenant, registrador, nombre, userId = null) => como(registrador, 'Fleet Management: registrando conductores…', () =>
    api.fleet.crearConductor({ tenantId: tenant.tenantId, registradoPorUserId: registrador.userId, userId, nombre, licencia: `C2-${azar(6, DIGITOS)}` }));
  const carlos = await conductor(cauca, marta, 'Carlos Ruiz', carlosUsuario.userId);
  const ana = await conductor(cauca, marta, 'Ana Torres');
  const diego = await conductor(andina, julian, 'Diego Salazar');
  const luis = await conductor(cauca, marta, 'Luis Mora');

  // 3. Cargo & Tracking recibe identificadores de Identity y de Fleet.
  const carga = (administrador, gen, tra, descripcion, origen, destino, pesoKg) => como(administrador, 'Cargo & Tracking: creando cargas…', () =>
    api.cargo.crearCarga({ generadorTenantId: gen.tenantId, transportistaTenantId: tra.tenantId, descripcion, origen, destino, pesoKg }));
  const c1 = await carga(laura, generadora, cauca, 'Residuos industriales no peligrosos', 'Cali', 'Popayán', 4200);
  const c2 = await carga(laura, generadora, cauca, 'Lodos de tratamiento de aguas', 'Cali', 'Palmira', 3100);
  const c3 = await carga(andres, ecoindustrias, andina, 'Aceites usados en tambores', 'Barranquilla', 'Cartagena', 2600);
  await carga(andres, ecoindustrias, andina, 'Chatarra electrónica (RAEE)', 'Barranquilla', 'Santa Marta', 1800);
  const c5 = await carga(andres, ecoindustrias, cauca, 'Residuos de construcción y demolición', 'Cali', 'Buenaventura', 7400);

  const asignar = (supervisor, c, v, d) => como(supervisor, 'Cargo & Tracking: asignando vehículo y conductor…', () =>
    api.cargo.asignar(c.cargaId, { vehiculoId: v.vehiculoId, conductorId: d.conductorId }));
  const mover = (quien, c, nuevoEstado, ubicacion, nota = null) => como(quien, 'Cargo & Tracking: registrando seguimiento…', () =>
    api.cargo.seguimiento(c.cargaId, { estado: nuevoEstado, ubicacion, nota }));

  // 4. Billing & Escrow retiene el pago. El de la primera carga se crea antes de entregarla, porque
  // el Saga que arranca con la entrega busca el pago por el identificador de la carga.
  const pago = (administrador, c, gen, tra, monto) => como(administrador, 'Billing & Escrow: reteniendo pagos…', () =>
    api.billing.crearPago({ generadorTenantId: gen.tenantId, transportistaTenantId: tra.tenantId, cargaId: c.cargaId, monto }));

  // La reserva de vehículo y conductor ya no la hace la consola: es una llamada entre servicios (Billing a Fleet, con un token
  // de servicio) y un navegador no tiene ese secreto. Fleet libera de forma idempotente, así que el Saga completa igual.
  await asignar(marta, c1, camion1, carlos);
  await mover(marta, c1, 'EnTransito', 'Santander de Quilichao');
  await pago(laura, c1, generadora, cauca, 850000);
  await mover(carlosUsuario, c1, 'Entregado', 'Planta de tratamiento, Popayán'); // dispara el Saga de liberación

  await asignar(marta, c2, camion2, ana);
  await mover(marta, c2, 'EnTransito', 'Salida de Cali, vía a Palmira');
  await mover(marta, c2, 'ConNovedad', 'Retén vehicular en la vía Cali–Palmira', 'Inspección de documentos retrasa la ruta.');
  await pago(laura, c2, generadora, cauca, 620000);

  await asignar(julian, c3, camion3, diego);
  await pago(andres, c3, ecoindustrias, andina, 480000);

  await asignar(marta, c5, camion4, luis);
  await mover(marta, c5, 'EnTransito', 'Peaje Loboguerrero');
  const p5 = await pago(andres, c5, ecoindustrias, cauca, 1250000);

  await como(andres, 'Billing & Escrow: reembolsando el pago de la carga cancelada…', () => api.billing.reembolsar(p5.pago.pagoId));
  await como(laura, 'Billing & Escrow: emitiendo la factura…', () =>
    api.billing.emitirFactura({ generadorTenantId: generadora.tenantId, transportistaTenantId: cauca.tenantId, cargaId: c1.cargaId, monto: 850000 }));
}
