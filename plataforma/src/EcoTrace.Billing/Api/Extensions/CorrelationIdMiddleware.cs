using System.Text.RegularExpressions;
using Serilog.Context;

namespace EcoTrace.Billing.Api.Extensions;

public sealed class CorrelationIdMiddleware(RequestDelegate next)
{
    public const string HeaderName = "X-Correlation-Id";
    public const string ItemKey = "EcoTrace.CorrelationId";
    private static readonly Regex Formato = new("^[A-Za-z0-9_-]{1,64}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public async Task InvokeAsync(HttpContext context)
    {
        var recibido = context.Request.Headers[HeaderName].ToString();
        var correlationId = Formato.IsMatch(recibido) ? recibido : Guid.NewGuid().ToString("N");
        context.Items[ItemKey] = correlationId;
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = correlationId;
            return Task.CompletedTask;
        });

        using (LogContext.PushProperty("CorrelationId", correlationId))
        {
            await next(context);
        }
    }
}