namespace SchemaForge.Models;

/// <summary>
/// Represents a collected SQL statement during dry run.
/// </summary>
public record SqlStatement(
    string Sql,
    string Category,
    string? ObjectName,
    DateTime Timestamp);

/// <summary>
/// Summary statistics for dry run.
/// </summary>
public class DryRunSummary
{
    public int TableCount { get; init; }
    public int IndexCount { get; init; }
    public int ConstraintCount { get; init; }
    public int ViewCount { get; init; }
    public int ForeignKeyCount { get; init; }
    public int TotalStatements { get; init; }
}

/// <summary>
/// Result of a dry run migration execution.
/// </summary>
public class DryRunResult
{
    /// <summary>
    /// All collected SQL statements.
    /// </summary>
    public IReadOnlyList<SqlStatement> Statements { get; init; } = [];

    /// <summary>
    /// Complete SQL script as a string.
    /// </summary>
    public string Script { get; init; } = string.Empty;

    /// <summary>
    /// Path to output file if written.
    /// </summary>
    public string? OutputFilePath { get; init; }

    /// <summary>
    /// Summary statistics.
    /// </summary>
    public DryRunSummary Summary { get; init; } = new();
}