// Conjunto de datos de ejemplo. Recorre el ciclo de negocio llamando a la API de cada servicio
// en orden y copiando a mano los identificadores de uno a otro, como hara despues el Saga.

import { ApiError, api } from './api.js';
import { abrirPanel, boton, confirmar } from './components.js';
import { aviso, h } from './dom.js';
import { estado, refrescar } from './store.js';

// Llamadas que hace `ejecutar`. Solo alimenta la barra de progreso: si cambia el guion, ajustelo.
const TOTAL_LLAMADAS = 36;

const azar = (largo, alfabeto) => Array.from({ length: largo }, () => alfabeto[Math.floor(Math.random() * alfabeto.length)]).join('');
const LETRAS = 'ABCDEFGHJKLMNPRSTUVWXYZ';
const DIGITOS = '0123456789';

export async function cargarDatosDeEjemplo() {
  if (estado.tenants.length > 0) {
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
  const sinTildes = (texto) => texto.toLowerCase().normalize('NFD').replace(/[̀-ͯ]/g, '').replace(/\s+/g, '.');

  // 1. Identity emite los identificadores base.
  const organizacion = (nombre, tenantType) =>
    paso('Identity: creando organizaciones…', () => api.identity.crearTenant({ nombre, tenantType }));
  const generadora = await organizacion('Generadora del Valle S.A.', 'Generador');
  const ecoindustrias = await organizacion('EcoIndustrias del Norte', 'Generador');
  const cauca = await organizacion('Transportes del Cauca', 'Transportista');
  const andina = await organizacion('Logística Andina', 'Transportista');

  const usuario = (tenant, role, nombre, dominio) => paso('Identity: creando usuarios…', () =>
    api.identity.crearUser({ tenantId: tenant.tenantId, role, nombre, email: `${sinTildes(nombre)}.${etiqueta}@${dominio}` }));
  await usuario(generadora, 'Administrador', 'Laura Mejía', 'generadoradelvalle.co');
  await usuario(ecoindustrias, 'Administrador', 'Andrés Pardo', 'ecoindustrias.co');
  const marta = await usuario(cauca, 'Supervisor', 'Marta Ríos', 'transportescauca.co');
  const julian = await usuario(andina, 'Supervisor', 'Julián Cárdenas', 'logisticaandina.co');
  const carlosUsuario = await usuario(cauca, 'Conductor', 'Carlos Ruiz', 'transportescauca.co');

  // 2. Fleet Management recibe solo los identificadores de Identity.
  const placa = () => `${azar(3, LETRAS)}${azar(3, DIGITOS)}`;
  const vehiculo = (tenant, registrador, capacidadKg) => paso('Fleet Management: registrando vehículos…', () =>
    api.fleet.crearVehiculo({ tenantId: tenant.tenantId, registradoPorUserId: registrador.userId, placa: placa(), capacidadKg }));
  const camion1 = await vehiculo(cauca, marta, 8000);
  const camion2 = await vehiculo(cauca, marta, 5000);
  const camion3 = await vehiculo(andina, julian, 12000);

  const conductor = (tenant, registrador, nombre, userId = null) => paso('Fleet Management: registrando conductores…', () =>
    api.fleet.crearConductor({ tenantId: tenant.tenantId, registradoPorUserId: registrador.userId, userId, nombre, licencia: `C2-${azar(6, DIGITOS)}` }));
  const carlos = await conductor(cauca, marta, 'Carlos Ruiz', carlosUsuario.userId);
  const ana = await conductor(cauca, marta, 'Ana Torres');
  const diego = await conductor(andina, julian, 'Diego Salazar');

  // 3. Cargo & Tracking recibe identificadores de Identity y de Fleet.
  const carga = (gen, tra, descripcion, origen, destino, pesoKg) => paso('Cargo & Tracking: creando cargas…', () =>
    api.cargo.crearCarga({ generadorTenantId: gen.tenantId, transportistaTenantId: tra.tenantId, descripcion, origen, destino, pesoKg }));
  const c1 = await carga(generadora, cauca, 'Residuos industriales no peligrosos', 'Cali', 'Popayán', 4200);
  const c2 = await carga(generadora, cauca, 'Lodos de tratamiento de aguas', 'Cali', 'Palmira', 3100);
  const c3 = await carga(ecoindustrias, andina, 'Aceites usados en tambores', 'Barranquilla', 'Cartagena', 2600);
  await carga(ecoindustrias, andina, 'Chatarra electrónica (RAEE)', 'Barranquilla', 'Santa Marta', 1800);
  const c5 = await carga(ecoindustrias, cauca, 'Residuos de construcción y demolición', 'Cali', 'Buenaventura', 7400);

  const asignar = (c, v, d) => paso('Cargo & Tracking: asignando vehículo y conductor…', () =>
    api.cargo.asignar(c.cargaId, { vehiculoId: v.vehiculoId, conductorId: d.conductorId }));
  const mover = (c, nuevoEstado, ubicacion, nota = null) => paso('Cargo & Tracking: registrando seguimiento…', () =>
    api.cargo.seguimiento(c.cargaId, { estado: nuevoEstado, ubicacion, nota }));

  await asignar(c1, camion1, carlos);
  await mover(c1, 'EnTransito', 'Santander de Quilichao');
  await mover(c1, 'Entregado', 'Planta de tratamiento, Popayán');

  await asignar(c2, camion2, ana);
  await mover(c2, 'EnTransito', 'Salida de Cali, vía a Palmira');
  await mover(c2, 'ConNovedad', 'Retén vehicular en la vía Cali–Palmira', 'Inspección de documentos retrasa la ruta.');

  await asignar(c3, camion3, diego);
  await asignar(c5, camion2, ana);
  await mover(c5, 'EnTransito', 'Peaje Loboguerrero');

  // 4. Billing & Escrow recibe los mismos identificadores.
  const pago = (c, gen, tra, monto) => paso('Billing & Escrow: reteniendo pagos…', () =>
    api.billing.crearPago({ generadorTenantId: gen.tenantId, transportistaTenantId: tra.tenantId, cargaId: c.cargaId, monto }));
  const p1 = await pago(c1, generadora, cauca, 850000);
  await pago(c2, generadora, cauca, 620000);
  await pago(c3, ecoindustrias, andina, 480000);
  const p5 = await pago(c5, ecoindustrias, cauca, 1250000);

  await paso('Billing & Escrow: liberando el pago de la carga entregada…', () => api.billing.liberar(p1.pago.pagoId));
  await paso('Billing & Escrow: reembolsando el pago de la carga cancelada…', () => api.billing.reembolsar(p5.pago.pagoId));
  await paso('Billing & Escrow: emitiendo la factura…', () =>
    api.billing.emitirFactura({ generadorTenantId: generadora.tenantId, transportistaTenantId: cauca.tenantId, cargaId: c1.cargaId, monto: 850000 }));
}
