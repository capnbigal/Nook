namespace Nook.Services;

/// <summary>Result of a backfill run: counts of rows created per step.</summary>
public sealed record BackfillResult(
    int NodesCreated,
    int NodeTagsCreated,
    int RelationsCreated,
    int ContainsRelationsCreated,
    int TaskActionsCreated,
    int ReminderActionsCreated,
    int ActivityLogsLinked,
    int SkippedDueDates,
    int UnmappedLinkLabels);

/// <summary>One parity/integrity check in the validation report.</summary>
public sealed record ValidationCheck(string Name, bool Passed, string Detail);

/// <summary>The outcome of validating the backfill.</summary>
public sealed record ValidationReport(IReadOnlyList<ValidationCheck> Checks, IReadOnlyList<string> AuditFindings)
{
    public bool AllPassed => Checks.All(c => c.Passed);
}

/// <summary>Current state of the graph migration, for the admin view.</summary>
public sealed record MigrationStatus(
    int ItemCount,
    int NodeCount,
    int RelationTypeCount,
    int VerbCount,
    bool SystemDataSeeded,
    bool BackfillLooksComplete);

/// <summary>
/// Explicit, idempotent migration of the legacy Item model into the graph model,
/// plus seeding of system reference data and parity validation. Never runs the
/// destructive/converting backfill automatically on startup — it is invoked
/// deliberately (admin page or a documented call).
/// </summary>
public interface IGraphMigrationService
{
    Task<int> SeedSystemDataAsync();
    Task<BackfillResult> BackfillAsync();
    Task<ValidationReport> ValidateAsync();
    Task<MigrationStatus> GetStatusAsync();
}
