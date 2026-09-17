using Grpc.Core;
using Grpc.Core.Interceptors;

namespace EcoTrace.Identity.Api.ErrorHandling;

public sealed class GrpcExceptionHandlingInterceptor : Interceptor
{
    private readonly ILogger<GrpcExceptionHandlingInterceptor> _logger;

    public GrpcExceptionHandlingInterceptor(ILogger<GrpcExceptionHandlingInterceptor> logger)
    {
        _logger = logger;
    }

    public override async Task<TResponse> UnaryServerHandler<TRequest, TResponse>(TRequest request, ServerCallContext context, UnaryServerMethod<TRequest, TResponse> continuation)
    {
        try
        {
            return await continuation(request, context);
        }
        catch (ArgumentException exception)
        {
            _logger.LogWarning(exception, "Error de validación gRPC en {Method}", context.Method);
            throw new RpcException(new Status(StatusCode.InvalidArgument, exception.Message));
        }
        catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
        {
            throw new RpcException(new Status(StatusCode.Cancelled, "La operación fue cancelada."));
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Error no controlado gRPC en {Method}", context.Method);
            throw new RpcException(new Status(StatusCode.Internal, "Ocurrió un error interno."));
        }
    }
}