using Microsoft.AspNetCore.Authorization;
using AssetIQ.Api.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace AssetIQ.Api.Controllers;

public sealed class HealthController : ApiControllerBase
{
    [HttpGet("healthz")]
    [AllowAnonymous]
    public HealthStatusDto Check() => new("ok");
}
