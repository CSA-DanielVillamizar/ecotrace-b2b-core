using EcoTrace.Cargo_Tracking.Domain.DTOs;
using EcoTrace.Cargo_Tracking.Domain.Services;
using EcoTrace.Cargo_Tracking.Grpc;
using Grpc.Core;

namespace EcoTrace.Cargo_Tracking.Api.Grpc;

public class CargoTrackingGrpcServer : CargoTrackingGrpcService.CargoTrackingGrpcServiceBase
{
    private readonly ICargoService _cargoService;

    public CargoTrackingGrpcServer(ICargoService cargoService)
    {
        _cargoService = cargoService;
    }

    public override async Task<GetCargoByIdResponse> GetCargoById(GetCargoByIdRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.Id, out var id))
        {
            return new GetCargoByIdResponse { Found = false };
        }

        var cargo = await _cargoService.GetByIdAsync(id, context.CancellationToken);
        return cargo is null
            ? new GetCargoByIdResponse { Found = false }
            : Map(cargo);
    }

    public override async Task<ListCargosResponse> ListCargos(ListCargosRequest request, ServerCallContext context)
    {
        var cargos = await _cargoService.GetAllAsync(cancellationToken: context.CancellationToken);
        var response = new ListCargosResponse();
        response.Cargos.AddRange(cargos.Select(Map));
        return response;
    }

    private static GetCargoByIdResponse Map(CargoResponse cargo)
    {
        return new GetCargoByIdResponse
        {
            Id = cargo.Id.ToString(),
            CargoCode = cargo.Description,
            Status = cargo.Status,
            Origin = cargo.OriginAddress,
            Destination = cargo.DestinationAddress,
            Found = true
        };
    }
}
