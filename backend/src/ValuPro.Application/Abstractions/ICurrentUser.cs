namespace ValuPro.Application.Abstractions;

/// <summary>The signed-in user for the current request (empty for background jobs and anonymous calls).</summary>
public interface ICurrentUser
{
    bool IsAuthenticated { get; }
    Guid? UserId { get; }
    string? Email { get; }
    /// <summary>Display name, for timelines ("Moved by Sneha Kulkarni").</summary>
    string? Name => null;
    bool HasPermission(string permission);
}
