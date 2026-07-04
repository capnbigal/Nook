using Microsoft.Data.SqlClient;
using Xunit;

namespace Nook.Tests;

/// <summary>One-time probe for SQL Server LocalDB availability.</summary>
public static class SqlServerAvailability
{
    private const string Master =
        @"Server=(localdb)\MSSQLLocalDB;Database=master;Trusted_Connection=True;TrustServerCertificate=True";

    public static readonly bool Available;
    public static readonly string Reason;

    static SqlServerAvailability()
    {
        try
        {
            using var conn = new SqlConnection(Master);
            conn.Open();
            Available = true;
            Reason = "";
        }
        catch (Exception ex)
        {
            Available = false;
            Reason = "SQL Server LocalDB (MSSQLLocalDB) is not reachable: " + ex.Message;
        }
    }
}

/// <summary>
/// A Fact that is reported as Skipped (with an explicit reason) when SQL Server
/// LocalDB is not available, so integration coverage is never a false pass.
/// </summary>
public sealed class SqlServerFactAttribute : FactAttribute
{
    public SqlServerFactAttribute()
    {
        if (!SqlServerAvailability.Available)
            Skip = SqlServerAvailability.Reason;
    }
}
