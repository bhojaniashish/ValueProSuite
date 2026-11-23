using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using ValuPro.Application.Common;

namespace ValuPro.Api.Controllers;

/// <summary>Shared helpers: request validation and turning business errors into problem responses.</summary>
[ApiController]
public abstract class ApiControllerBase : ControllerBase
{
    protected async Task<IActionResult?> ValidateAsync<T>(T request, CancellationToken ct)
    {
        var validator = HttpContext.RequestServices.GetService<IValidator<T>>();
        if (validator is null) return null;
        var result = await validator.ValidateAsync(request, ct);
        return result.IsValid ? null : ValidationProblem(new ValidationProblemDetails(result.ToDictionary()));
    }

    protected IActionResult Problem(Error error)
    {
        if (error.Kind == ErrorKind.Validation && error.Field is not null)
            return ValidationProblem(new ValidationProblemDetails(
                new Dictionary<string, string[]> { [error.Field] = [error.Message] }) { Title = error.Message });

        var status = error.Kind switch
        {
            ErrorKind.NotFound => StatusCodes.Status404NotFound,
            ErrorKind.Conflict => StatusCodes.Status409Conflict,
            ErrorKind.Forbidden => StatusCodes.Status403Forbidden,
            _ => StatusCodes.Status400BadRequest,
        };
        var problem = new ProblemDetails { Status = status, Title = error.Message };
        if (error.Field is not null) problem.Extensions["field"] = error.Field;
        return new ObjectResult(problem) { StatusCode = status };
    }

    protected IActionResult From<T>(Result<T> result) => result.IsSuccess ? Ok(result.Value) : Problem(result.Error!);
}
