namespace EcoTrace.FleetManagement.API.Controllers;
using EcoTrace.FleetManagement.Domain;
using EcoTrace.FleetManagement.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;

[ApiController]
[Route("api/[controller]")]
public class TrucksController : ControllerBase
{
    private readonly FleetDbContext _context;

    public TrucksController(FleetDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<IActionResult> GetTrucks()
    {
        var trucks = await _context.Trucks.ToListAsync();
        return Ok(trucks);
    }

    [HttpPost]
    public async Task<IActionResult> CreateTruck([FromBody] CreateTruckDto request)
    {
        var truck = new Truck(request.TenantId, request.LicensePlate, request.Model, request.CapacityTons);
        _context.Trucks.Add(truck);
        await _context.SaveChangesAsync();
        return CreatedAtAction(nameof(GetTrucks), new { id = truck.Id }, truck);
    }
}

public class CreateTruckDto
{
    public Guid TenantId { get; set; }
    public string LicensePlate { get; set; }
    public string Model { get; set; }
    public double CapacityTons { get; set; }
}