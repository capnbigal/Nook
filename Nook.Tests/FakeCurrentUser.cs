using Nook.Services;

namespace Nook.Tests;

public sealed class FakeCurrentUser : ICurrentUser
{
    private readonly string? _userId;
    public FakeCurrentUser(string? userId) => _userId = userId;

    public Task<string?> GetUserIdAsync() => Task.FromResult(_userId);

    public Task<string> GetRequiredUserIdAsync() =>
        _userId is null
            ? throw new InvalidOperationException("No current user.")
            : Task.FromResult(_userId);
}
