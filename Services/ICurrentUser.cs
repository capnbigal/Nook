namespace Nook.Services;

/// <summary>Resolves the signed-in user's id for the service layer.</summary>
public interface ICurrentUser
{
    /// <summary>The current user id, or null if not authenticated.</summary>
    Task<string?> GetUserIdAsync();

    /// <summary>The current user id; throws if not authenticated.</summary>
    Task<string> GetRequiredUserIdAsync();
}
