namespace SchemaForge.Models;

/// <summary>
/// Represents a database index definition.
/// Contains all metadata needed to recreate the index in a target database.
/// </summary>
public record IndexSchema
{
    /// <summary>The name of the index.</summary>
    public required string IndexName { get; init; }

    /// <summary>The table on which the index is defined.</summary>
    public required string TableName { get; init; }

    /// <summary>The schema containing the table.</summary>
    public required string SchemaName { get; init; }

    /// <summary>The columns included in the index key (in order).</summary>
    public List<string> Columns { get; init; } = [];

    /// <summary>Whether the index enforces uniqueness.</summary>
    public required bool IsUnique { get; init; }

    /// <summary>Whether this is a clustered index (SQL Server specific).</summary>
    public required bool IsClustered { get; init; }

    /// <summary>Whether this index backs the primary key constraint.</summary>
    public required bool IsPrimaryKey { get; init; }

    /// <summary>Filter expression for partial/filtered indexes (WHERE clause).</summary>
    public string? FilterExpression { get; init; }

    /// <summary>Non-key columns included for covering index scenarios (INCLUDE clause).</summary>
    public List<string> IncludedColumns { get; init; } = [];
}

/// <summary>
/// Represents a database view definition.
/// Contains the view name and its SQL definition for migration.
/// </summary>
public record ViewSchema
{
    /// <summary>The name of the view.</summary>
    public required string ViewName { get; init; }

    /// <summary>The schema containing the view.</summary>
    public required string SchemaName { get; init; }

    /// <summary>The SQL definition (SELECT statement) of the view.</summary>
    public required string Definition { get; init; }

    /// <summary>List of column names exposed by the view.</summary>
    public List<string> Columns { get; init; } = [];
}

/// <summary>
/// Represents a database constraint (CHECK, UNIQUE, or DEFAULT).
/// Does not include primary key or foreign key constraints (handled separately).
/// </summary>
public record ConstraintSchema
{
    /// <summary>The name of the constraint.</summary>
    public required string ConstraintName { get; init; }

    /// <summary>The table on which the constraint is defined.</summary>
    public required string TableName { get; init; }

    /// <summary>The schema containing the table.</summary>
    public required string SchemaName { get; init; }

    /// <summary>The type of constraint (Check, Unique, or Default).</summary>
    public required ConstraintType Type { get; init; }

    /// <summary>The columns involved in the constraint.</summary>
    public List<string> Columns { get; init; } = [];

    /// <summary>The expression for CHECK constraints (e.g., "age >= 0").</summary>
    public string? CheckExpression { get; init; }

    /// <summary>The expression for DEFAULT constraints (e.g., "GETDATE()").</summary>
    public string? DefaultExpression { get; init; }

    /// <summary>The source column data type (e.g., "bit"), used for type-aware default conversion.</summary>
    public string? ColumnDataType { get; init; }
}

/// <summary>
/// Enumeration of supported constraint types.
/// </summary>
public enum ConstraintType
{
    /// <summary>CHECK constraint - validates data against an expression.</summary>
    Check,
    /// <summary>UNIQUE constraint - ensures column values are unique.</summary>
    Unique,
    /// <summary>DEFAULT constraint - provides default value for a column.</summary>
    Default
}