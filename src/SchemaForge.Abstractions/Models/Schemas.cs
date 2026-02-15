namespace SchemaForge.Abstractions.Models;

/// <summary>
/// Constants for supported database type identifiers.
/// Used as keys for keyed dependency injection of schema readers/writers.
/// </summary>
public static class DatabaseTypes
{
    public const string SqlServer = "sqlserver";
    public const string PostgreSql = "postgres";
    public const string MySql = "mysql";
    public const string Oracle = "oracle";
}

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

/// <summary>
/// Defines the SQL dialect characteristics of a specific database system.
/// Used for converting SQL expressions between different database platforms.
/// </summary>
public class DatabaseDialect
{
    /// <summary>Display name of the database (e.g., "SQL Server", "PostgreSQL").</summary>
    public required string Name { get; init; }

    /// <summary>Character used to quote identifiers at the start (e.g., "[" for SQL Server).</summary>
    public required string IdentifierQuoteStart { get; init; }

    /// <summary>Character used to quote identifiers at the end (e.g., "]" for SQL Server).</summary>
    public required string IdentifierQuoteEnd { get; init; }

    /// <summary>Function to get current date (e.g., "GETDATE()" vs "NOW()").</summary>
    public required string CurrentDateFunction { get; init; }

    /// <summary>Function to get current timestamp with time zone.</summary>
    public required string CurrentTimestampFunction { get; init; }

    /// <summary>Function to generate a new GUID/UUID.</summary>
    public required string NewGuidFunction { get; init; }

    /// <summary>Function for null coalescing (e.g., "ISNULL" vs "COALESCE").</summary>
    public required string NullCheckFunction { get; init; }

    /// <summary>Operator for string concatenation (e.g., "+" vs "||" vs "CONCAT").</summary>
    public required string StringConcatOperator { get; init; }

    /// <summary>Keyword for limiting result rows (e.g., "TOP" vs "LIMIT").</summary>
    public required string LimitClause { get; init; }

    /// <summary>Template for OFFSET/FETCH pagination clause.</summary>
    public required string OffsetFetchClause { get; init; }

    /// <summary>Literal for boolean TRUE (e.g., "1" vs "TRUE").</summary>
    public required string BooleanTrue { get; init; }

    /// <summary>Literal for boolean FALSE (e.g., "0" vs "FALSE").</summary>
    public required string BooleanFalse { get; init; }
}