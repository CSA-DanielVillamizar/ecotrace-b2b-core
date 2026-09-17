using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using EcoTrace.CargoTracking.Domain;
using EcoTrace.CargoTracking.Infrastructure;
using EcoTrace.CargoTracking.API.Dtos;

namespace EcoTrace.CargoTracking.API.Controllers;

[ApiController]
[Route("api/cargas")]
public class CargasController : ControllerBase
{
    private readonly CargoTrackingDbContext _db;

    public CargasController(CargoTrackingDbContext db)
    {
        _db = db;
    }

    // POST /api/cargas — registra una nueva carga.
    [HttpPost]
    public async Task<IActionResult> Create(CreateCargaDto dto)
    {
        var carga = new Carga
        {
            GeneradorTenantId = dto.GeneradorTenantId,
            TransportistaTenantId = dto.TransportistaTenantId,
            Origen = dto.Origen,
            Destino = dto.Destino
        };

        _db.Cargas.Add(carga);
        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetById), new { id = carga.Id }, carga);
    }

    // GET /api/cargas/{id} — consulta una carga con sus asignaciones y seguimientos.
    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        var carga = await _db.Cargas
            .Include(c => c.Asignaciones)
            .Include(c => c.Seguimientos)
            .FirstOrDefaultAsync(c => c.Id == id);

        return carga is null ? NotFound() : Ok(carga);
    }

    // GET /api/cargas — lista todas las cargas.
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var cargas = await _db.Cargas.ToListAsync();
        return Ok(cargas);
    }

    // POST /api/cargas/{id}/asignaciones — asigna vehículo + conductor a una carga.
    [HttpPost("{id}/asignaciones")]
    public async Task<IActionResult> Asignar(Guid id, CreateAsignacionDto dto)
    {
        var carga = await _db.Cargas.FindAsync(id);
        if (carga is null) return NotFound("La carga no existe.");

        var asignacion = new AsignacionCarga
        {
            CargaId = id,
            VehiculoId = dto.VehiculoId,
            ConductorId = dto.ConductorId
        };

        _db.Asignaciones.Add(asignacion);
        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetById), new { id }, asignacion);
    }

    // POST /api/cargas/{id}/seguimientos — registra un nuevo punto de seguimiento.
    [HttpPost("{id}/seguimientos")]
    public async Task<IActionResult> RegistrarSeguimiento(Guid id, CreateSeguimientoDto dto)
    {
        var carga = await _db.Cargas.FindAsync(id);
        if (carga is null) return NotFound("La carga no existe.");

        var seguimiento = new Seguimiento
        {
            CargaId = id,
            Ubicacion = dto.Ubicacion,
            Estado = dto.Estado,
            Descripcion = dto.Descripcion
        };

        _db.Seguimientos.Add(seguimiento);
        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetById), new { id }, seguimiento);
    }
}