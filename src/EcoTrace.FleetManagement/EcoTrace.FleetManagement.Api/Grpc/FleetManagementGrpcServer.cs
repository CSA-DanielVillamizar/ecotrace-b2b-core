using EcoTrace.FleetManagement.Domain.UseCases.Contracts;
using EcoTrace.FleetManagement.Domain.Interfaces.Services;
using EcoTrace.FleetManagement.Grpc;
using Grpc.Core;

namespace EcoTrace.FleetManagement.Api.Grpc;

public class FleetManagementGrpcServer : FleetManagementGrpcService.FleetManagementGrpcServiceBase
{
    private readonly IVehicleService _vehicleService;

    public FleetManagementGrpcServer(IVehicleService vehicleService)
    {
        _vehicleService = vehicleService;
    }

    public override async Task<GetVehicleByIdResponse> GetVehicleById(GetVehicleByIdRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.Id, out var id))
        {
            return new GetVehicleByIdResponse { Found = false };
        }

        var vehicle = await _vehicleService.GetByIdAsync(id, context.CancellationToken);
        return vehicle is null
            ? new GetVehicleByIdResponse { Found = false }
            : Map(vehicle);
    }

    public override async Task<ListVehiclesResponse> ListVehicles(ListVehiclesRequest request, ServerCallContext context)
    {
        var vehicles = await _vehicleService.GetAllAsync(cancellationToken: context.CancellationToken);
        var response = new ListVehiclesResponse();
        response.Vehicles.AddRange(vehicles.Select(Map));
        return response;
    }

    private static GetVehicleByIdResponse Map(VehicleResponse vehicle)
    {
        return new GetVehicleByIdResponse
        {
            Id = vehicle.Id.ToString(),
            Plate = vehicle.PlateNumber,
            Status = vehicle.Status.ToString(),
            Model = vehicle.Model,
            Found = true
        };
    }
}
