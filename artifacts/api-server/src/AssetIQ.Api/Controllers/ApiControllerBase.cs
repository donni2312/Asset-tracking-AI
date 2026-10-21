using AssetIQ.Api.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace AssetIQ.Api.Controllers;

/// <summary>
/// Every endpoint lives under /api. Controllers stay thin: they read the request, call a service and
/// return its result; failures are <see cref="ApiException"/>s rendered by <see cref="ApiErrorMiddleware"/>.
/// </summary>
[ApiController]
[Route("api")]
[Produces("application/json")]
public abstract class ApiControllerBase : ControllerBase
{
    protected CancellationToken Aborted => HttpContext.RequestAborted;

    /// <summary>201 with the created resource as the body (no Location header, as before).</summary>
    protected static ObjectResult Status201(object value) => new(value) { StatusCode = StatusCodes.Status201Created };
}
