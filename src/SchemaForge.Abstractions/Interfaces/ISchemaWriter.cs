using SchemaForge.Abstractions.Models;

namespace SchemaForge.Abstractions.Interfaces;

/// <summary>
/// Interface for writing database schema objects to a target database.
/// Creates tables, views, indexes, and constraints.
/// </summary>
public interface ISchemaWriter
{
    /// <summary>
    /// Creates tables with columns, primary keys, and foreign keys in the target database.
    /// </summary>
    Task CreateSchemaAsync(string connectionString, string schemaName, List<TableSchema> tables);

    /// <summary>
    /// Creates views in the target database with converted SQL definitions.
    /// </summary>
    Task CreateViewsAsync(string connectionString, string schemaName, List<ViewSchema> views,
        List<TableSchema>? sourceTables = null);

    /// <summary>
    /// Creates indexes on tables in the target database.
    /// </summary>
    Task CreateIndexesAsync(string connectionString, string schemaName, List<IndexSchema> indexes);

    /// <summary>
    /// Creates constraints (check, unique, default) on tables in the target database.
    /// </summary>
    Task CreateConstraintsAsync(string connectionString, string schemaName, List<ConstraintSchema> constraints);
}