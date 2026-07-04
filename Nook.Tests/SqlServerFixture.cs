using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Nook.Data;

namespace Nook.Tests;

/// <summary>
/// An IDbContextFactory backed by a real SQL Server LocalDB database, for
/// migration/backfill integration tests. Availability is detected at construction;
/// if LocalDB cannot be reached, <see cref="Available"/> is false and tests skip.
/// </summary>
public sealed class SqlServerFixture : IDbContextFactory<NookContext>, IDisposable
{
    private const string Master =
        @"Server=(localdb)\MSSQLLocalDB;Database=master;Trusted_Connection=True;TrustServerCertificate=True";

    public bool Available { get; }
    public string? SkipReason { get; }
    private readonly string _dbName = "NookTest_" + Guid.NewGuid().ToString("N");
    private readonly string _connectionString;
    private readonly DbContextOptions<NookContext> _options;

    public SqlServerFixture()
    {
        _connectionString =
            $@"Server=(localdb)\MSSQLLocalDB;Database={_dbName};Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=True";
        _options = new DbContextOptionsBuilder<NookContext>().UseSqlServer(_connectionString).Options;

        try
        {
            using var conn = new SqlConnection(Master);
            conn.Open();
            using var db = new NookContext(_options);
            db.Database.Migrate();
            Available = true;
        }
        catch (Exception ex)
        {
            Available = false;
            SkipReason = "SQL Server LocalDB (MSSQLLocalDB) is not reachable: " + ex.Message;
        }
    }

    public NookContext CreateDbContext() => new(_options);
    public Task<NookContext> CreateDbContextAsync(CancellationToken ct = default) => Task.FromResult(CreateDbContext());

    public void Dispose()
    {
        if (!Available) return;
        try
        {
            using var conn = new SqlConnection(Master);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText =
                $"ALTER DATABASE [{_dbName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{_dbName}];";
            cmd.ExecuteNonQuery();
        }
        catch { /* best-effort cleanup */ }
    }
}
