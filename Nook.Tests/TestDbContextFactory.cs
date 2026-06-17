using Microsoft.EntityFrameworkCore;
using Nook.Data;

namespace Nook.Tests;

/// <summary>
/// An IDbContextFactory backed by a uniquely-named EF Core InMemory database,
/// so each test gets isolated state. Matches the production factory pattern.
/// </summary>
public sealed class TestDbContextFactory : IDbContextFactory<NookContext>
{
    private readonly DbContextOptions<NookContext> _options;

    public TestDbContextFactory()
    {
        _options = new DbContextOptionsBuilder<NookContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
    }

    public NookContext CreateDbContext() => new(_options);

    public Task<NookContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(CreateDbContext());
}
