using EcoTrace.Cargo_Tracking.Domain.DTOs;
using EcoTrace.Cargo_Tracking.Domain.Constants;
using EcoTrace.Cargo_Tracking.Domain.Enumerations;
using EcoTrace.Cargo_Tracking.Domain.Models;
using EcoTrace.Cargo_Tracking.Domain.Repositories;

namespace EcoTrace.Cargo_Tracking.Domain.Services;

public class CargoAssignmentService : ICargoAssignmentService
{
    private readonly ICargoAssignmentRepository _assignmentRepository;
    private readonly ICargoRepository _cargoRepository;

    public CargoAssignmentService(
        ICargoAssignmentRepository assignmentRepository,
        ICargoRepository cargoRepository)
    {
        _assignmentRepository = assignmentRepository;
        _cargoRepository = cargoRepository;
    }

    public async Task<List<CargoAssignmentResponse>> GetAllAsync(Guid? cargoId = null, Guid? driverId = null, Guid? vehicleId = null, CancellationToken cancellationToken = default)
    {
        var assignments = await _assignmentRepository.GetAllAsync(cargoId, driverId, vehicleId, cancellationToken);
        return assignments.Select(ToResponse).ToList();
    }

    public async Task<CargoAssignmentResponse?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var assignment = await _assignmentRepository.GetByIdAsync(id, cancellationToken);
        return assignment is null ? null : ToResponse(assignment);
    }

    public async Task<CargoAssignmentResponse> CreateAsync(CreateCargoAssignmentRequest request, CancellationToken cancellationToken = default)
    {
        var cargo = await _cargoRepository.GetByIdAsync(request.CargoId, cancellationToken);
        if (cargo is null)
        {
            throw new ArgumentException(ErrorMessages.Cargo.CargoNotFound);
        }

        var activeAssignment = await _assignmentRepository.GetActiveByCargoIdAsync(request.CargoId, cancellationToken);
        if (activeAssignment != null)
        {
            throw new ArgumentException(ErrorMessages.CargoAssignment.CargoAlreadyAssigned);
        }

        var assignment = new CargoAssignment
        {
            Id = Guid.NewGuid(),
            CargoId = request.CargoId,
            DriverId = request.DriverId,
            VehicleId = request.VehicleId,
            Notes = request.Notes?.Trim(),
            Status = AssignmentStatus.Active,
            AssignedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };

        cargo.Status = CargoStatus.Assigned;
        cargo.UpdatedAt = DateTime.UtcNow;

        _cargoRepository.Update(cargo);
        await _assignmentRepository.AddAsync(assignment, cancellationToken);
        await _assignmentRepository.SaveChangesAsync(cancellationToken);

        return ToResponse(assignment);
    }

    public async Task<CargoAssignmentResponse?> UpdateStatusAsync(Guid id, UpdateAssignmentStatusRequest request, CancellationToken cancellationToken = default)
    {
        var assignment = await _assignmentRepository.GetByIdAsync(id, cancellationToken);
        if (assignment is null)
        {
            return null;
        }

        assignment.Status = request.Status;
        assignment.UpdatedAt = DateTime.UtcNow;

        _assignmentRepository.Update(assignment);
        await _assignmentRepository.SaveChangesAsync(cancellationToken);

        return ToResponse(assignment);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var assignment = await _assignmentRepository.GetByIdAsync(id, cancellationToken);
        if (assignment is null)
        {
            return false;
        }

        assignment.IsDeleted = true;
        assignment.DeletedAt = DateTime.UtcNow;

        _assignmentRepository.Update(assignment);
        await _assignmentRepository.SaveChangesAsync(cancellationToken);

        return true;
    }

    private static CargoAssignmentResponse ToResponse(CargoAssignment ca) =>
        new(ca.Id, ca.CargoId, ca.DriverId, ca.VehicleId, ca.AssignedAt, ca.Status.ToString(), ca.Notes, ca.CreatedAt, ca.UpdatedAt);
}