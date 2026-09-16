using EcoTrace.Identity.Domain.DTOs;
using EcoTrace.Identity.Domain.Services;
using EcoTrace.Identity.Grpc;
using Grpc.Core;

namespace EcoTrace.Identity.Api.Grpc;

public class IdentityGrpcServer : IdentityGrpcService.IdentityGrpcServiceBase
{
    private readonly IUserService _userService;

    public IdentityGrpcServer(IUserService userService)
    {
        _userService = userService;
    }

    public override async Task<GetUserByIdResponse> GetUserById(GetUserByIdRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.Id, out var id))
        {
            return new GetUserByIdResponse { Found = false };
        }

        var user = await _userService.GetByIdAsync(id, context.CancellationToken);
        return user is null
            ? new GetUserByIdResponse { Found = false }
            : Map(user);
    }

    public override async Task<ListUsersResponse> ListUsers(ListUsersRequest request, ServerCallContext context)
    {
        var users = await _userService.GetAllAsync(cancellationToken: context.CancellationToken);
        var response = new ListUsersResponse();
        response.Users.AddRange(users.Select(Map));
        return response;
    }

    private static GetUserByIdResponse Map(UserResponse user)
    {
        return new GetUserByIdResponse
        {
            Id = user.Id.ToString(),
            Email = user.Email,
            FirstName = user.FullName,
            LastName = string.Empty,
            Found = true
        };
    }
}
