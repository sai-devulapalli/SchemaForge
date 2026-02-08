namespace SchemaForge.Services.Interfaces;

/// <summary>
/// Interface for providing database-specific standards and conventions.
/// Used to apply appropriate naming rules and identifier handling per database type.
/// </summary>
public interface IDatabaseStandardsProvider
{
    /// <summary>
    /// Gets the standards and conventions for a specific database type.
    /// </summary>
    /// <param name="databaseType">Database type identifier (e.g., "postgres", "sqlserver").</param>
    /// <returns>Database standards configuration.</returns>
    DatabaseStandards GetStandards(string databaseType);
}

/// <summary>
/// Contains database-specific standards including naming conventions,
/// identifier rules, reserved keywords, and feature support.
/// </summary>
public record DatabaseStandards
{
    /// <summary>Display name of the database type.</summary>
    public required string DatabaseType { get; init; }

    /// <summary>Standard naming convention for this database.</summary>
    public required NamingConvention NamingConvention { get; init; }

    /// <summary>Maximum length for identifiers (table names, column names, etc.).</summary>
    public required int MaxIdentifierLength { get; init; }

    /// <summary>Array of reserved SQL keywords that require quoting.</summary>
    public required string[] ReservedKeywords { get; init; }

    /// <summary>Whether identifiers are case-sensitive.</summary>
    public required bool CaseSensitive { get; init; }

    /// <summary>Character to start quoting identifiers.</summary>
    public required string IdentifierQuoteStart { get; init; }

    /// <summary>Character to end quoting identifiers.</summary>
    public required string IdentifierQuoteEnd { get; init; }

    /// <summary>Schema support level: "full", "database", "user", or "none".</summary>
    public required string SchemaSupport { get; init; }

    /// <summary>Whether the database supports identity/auto-increment columns.</summary>
    public required bool SupportsIdentity { get; init; }

    /// <summary>Identity strategy: "identity", "sequence", or "auto_increment".</summary>
    public required string IdentityStrategy { get; init; }
}

/// <summary>
/// Enumeration of supported naming conventions for database identifiers.
/// </summary>
public enum NamingConvention
{
    /// <summary>PostgreSQL style: lower_snake_case (table_name, column_name).</summary>
    SnakeCase,
    /// <summary>SQL Server style: PascalCase (TableName, ColumnName).</summary>
    PascalCase,
    /// <summary>MySQL style: lowercase (tablename, columnname).</summary>
    Lowercase,
    /// <summary>Oracle style: UPPERCASE (TABLENAME, COLUMNNAME).</summary>
    Uppercase,
    /// <summary>Keep original naming from source database.</summary>
    Preserve
}