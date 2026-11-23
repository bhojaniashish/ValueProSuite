using Microsoft.AspNetCore.Mvc;
using ValuPro.Api.Security;
using ValuPro.Application.Users;
using ValuPro.Domain.Security;

namespace ValuPro.Api.Controllers;

[Route("api/roles")]
[HasPermission(Permissions.Users.View)]
public sealed class RolesController(IRolePermissionService permissions) : ApiControllerBase
{
    /// <summary>The role × module permission matrix.</summary>
    [HttpGet("permissions")]
    [ProducesResponseType<PermissionMatrix>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Matrix(CancellationToken ct) => Ok(await permissions.GetMatrixAsync(ct));

    /// <summary>Replaces one role's permissions. Applies to signed-in users within a minute.</summary>
    [HttpPut("{role}/permissions")]
    [HasPermission(Permissions.Users.Manage)]
    [ProducesResponseType<PermissionMatrix>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Update(string role, UpdateRolePermissionsRequest request, CancellationToken ct) =>
        From(await permissions.UpdateRoleAsync(role, request.Permissions ?? [], ct));
}
