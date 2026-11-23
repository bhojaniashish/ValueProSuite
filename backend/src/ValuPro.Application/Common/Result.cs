namespace ValuPro.Application.Common;

public enum ErrorKind
{
    Validation,
    NotFound,
    Conflict,
    Forbidden,
}

public sealed record Error(ErrorKind Kind, string Message, string? Field = null)
{
    public static Error NotFound(string message) => new(ErrorKind.NotFound, message);
    public static Error Conflict(string message, string? field = null) => new(ErrorKind.Conflict, message, field);
    public static Error Validation(string message, string? field = null) => new(ErrorKind.Validation, message, field);
    public static Error Forbidden(string message) => new(ErrorKind.Forbidden, message);
}

/// <summary>Outcome of a use case: a value, or a business error the API turns into a problem response.</summary>
public readonly record struct Result<T>
{
    public T? Value { get; }
    public Error? Error { get; }
    public bool IsSuccess => Error is null;

    private Result(T? value, Error? error) => (Value, Error) = (value, error);

    /// <summary>For interface-typed values, which C# cannot convert implicitly.</summary>
    public static Result<T> Ok(T value) => new(value, null);

    public static implicit operator Result<T>(T value) => new(value, null);
    public static implicit operator Result<T>(Error error) => new(default, error);
}

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize);
