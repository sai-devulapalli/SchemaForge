using SchemaForge.Abstractions.Models;

namespace SchemaForge.Abstractions.Configuration;

/// <summary>
/// Configuration settings for database migration.
/// Loaded from appsettings.json or environment variables.
/// </summary>
public class MigrationSettings
{
    /// <summary>Type of source database: "sqlserver", "postgres", "mysql", or "oracle".</summary>
    public string SourceDatabaseType { get; set; } = DatabaseTypes.SqlServer;

    /// <summary>Type of target database: "sqlserver", "postgres", "mysql", or "oracle".</summary>
    public string TargetDatabaseType { get; set; } = DatabaseTypes.PostgreSql;

    /// <summary>Connection string for the source database.</summary>
    public string SourceConnectionString { get; set; } = string.Empty;

    /// <summary>Connection string for the target database.</summary>
    public string TargetConnectionString { get; set; } = string.Empty;

    /// <summary>Schema name to use in target database (e.g., "public" for PostgreSQL, "dbo" for SQL Server).</summary>
    public string TargetSchemaName { get; set; } = "public";

    /// <summary>Number of rows to process per batch during data migration.</summary>
    public int BatchSize { get; set; } = 1000;

    /// <summary>
    /// Naming convention for identifiers: "auto", "snake_case", "camelCase", "PascalCase", "lowercase", "UPPERCASE", or "preserve".
    /// "auto" uses the target database's standard convention.
    /// </summary>
    public string NamingConvention { get; set; } = "auto";

    /// <summary>When true, applies target database naming standards automatically.</summary>
    public bool UseTargetDatabaseStandards { get; set; } = true;

    /// <summary>When true, preserves original identifier casing from source database.</summary>
    public bool PreserveSourceCase { get; set; } = false;

    /// <summary>Maximum identifier length. 0 uses target database default.</summary>
    public int MaxIdentifierLength { get; set; } = 0;

    /// <summary>
    /// Source schema name filter for reading tables.
    /// null  = use the connection's default schema (SCHEMA_NAME()) — default behaviour, safe for tests.
    /// "*"   = read all non-system user schemas (use for multi-schema databases like WideWorldImporters).
    /// other = filter to that exact schema name.
    /// </summary>
    public string? SourceSchemaName { get; set; } = null;

    // Legacy property aliases for backward compatibility
    /// <summary>Alias for SourceConnectionString (legacy support).</summary>
    public string SqlServerConnectionString { get => SourceConnectionString; set => SourceConnectionString = value; }

    /// <summary>Alias for TargetConnectionString (legacy support).</summary>
    public string PostgresConnectionString { get => TargetConnectionString; set => TargetConnectionString = value; }

    /// <summary>Alias for TargetSchemaName (legacy support).</summary>
    public string PostgresSchemaName { get => TargetSchemaName; set => TargetSchemaName = value; }
}