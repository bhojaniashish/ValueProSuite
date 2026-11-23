using Microsoft.AspNetCore.Mvc;
using ValuPro.Api.Security;
using ValuPro.Application.Common;
using ValuPro.Application.Users;
using ValuPro.Domain.Security;

namespace ValuPro.Api.Controllers;

[Route("api/users")]
[HasPermission(Permissions.Users.View)]
public sealed class UsersController(IUserAdminService users) : ApiControllerBase
{
    /// <summary>Users of the firm, with search, role/status filters, sorting and paging.</summary>
    [HttpGet]
    [ProducesResponseType<PagedResult<UserListItem>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] UserQuery query, CancellationToken ct) =>
        await ValidateAsync(query, ct) ?? Ok(await users.ListAsync(query, ct));

    /// <summary>Counts for the stat cards above the user table.</summary>
    [HttpGet("summary")]
    [ProducesResponseType<UserSummary>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Summary(CancellationToken ct) => Ok(await users.GetSummaryAsync(ct));

    [HttpGet("{id:guid}")]
    [ProducesResponseType<UserListItem>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct) => From(await users.GetAsync(id, ct));

    /// <summary>Adds a team member. The response carries a one-time temporary password to hand over.</summary>
    [HttpPost]
    [HasPermission(Permissions.Users.Manage)]
    [ProducesResponseType<TemporaryPasswordResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Create(SaveUserRequest request, CancellationToken ct) =>
        await ValidateAsync(request, ct) ?? From(await users.CreateAsync(request, ct));

    /// <summary>Edits details, role and status. Setting Inactive signs the user out within a minute.</summary>
    [HttpPut("{id:guid}")]
    [HasPermission(Permissions.Users.Manage)]
    [ProducesResponseType<UserListItem>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Update(Guid id, SaveUserRequest request, CancellationToken ct) =>
        await ValidateAsync(request, ct) ?? From(await users.UpdateAsync(id, request, ct));

    /// <summary>Issues a new temporary password and ends the user's sessions.</summary>
    [HttpPost("{id:guid}/reset-password")]
    [HasPermission(Permissions.Users.Manage)]
    [ProducesResponseType<TemporaryPasswordResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ResetPassword(Guid id, CancellationToken ct) =>
        From(await users.ResetPasswordAsync(id, ct));
}
