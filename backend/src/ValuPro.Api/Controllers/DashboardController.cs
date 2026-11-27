using Microsoft.AspNetCore.Mvc;
using ValuPro.Api.Security;
using ValuPro.Application.Dashboard;
using ValuPro.Domain.Security;

namespace ValuPro.Api.Controllers;

/// <summary>The home page: what is late or due, visits, pipeline, banks, workload, trend, activity and money owed.</summary>
[Route("api/dashboard")]
[HasPermission(Permissions.Dashboard.View)]
public sealed class DashboardController(IDashboardService dashboard) : ApiControllerBase
{
    /// <param name="mine">Only cases where you are the valuer or reviewer.</param>
    [HttpGet]
    [ProducesResponseType<DashboardSummary>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get([FromQuery] bool mine, CancellationToken ct) => Ok(await dashboard.SummaryAsync(mine, ct));
}
