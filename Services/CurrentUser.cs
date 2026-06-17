using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;

namespace Nook.Services;

/// <summary>
/// Resolves the current user id from the cascaded Blazor AuthenticationState.
/// </summary>
public sealed class CurrentUser : ICurrentUser
{
    private readonly AuthenticationStateProvider _authProvider;

    public CurrentUser(AuthenticationStateProvider authProvider)
    {
        _authProvider = authProvider;
    }

    public async Task<string?> GetUserIdAsync()
    {
        var state = await _authProvider.GetAuthenticationStateAsync();
        return state.User.FindFirstValue(ClaimTypes.NameIdentifier);
    }

    public async Task<string> GetRequiredUserIdAsync()
        => await GetUserIdAsync()
           ?? throw new InvalidOperationException("No authenticated user in the current context.");
}
