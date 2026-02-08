namespace SchemaForge.Models;

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