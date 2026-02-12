using SchemaForge.Models;

namespace SchemaForge.Services.Interfaces;

/// <summary>
/// Interface for writing database schema objects to a target database.
/// Creates tables, views, indexes, and constraints.
/// </summary>
public interface ISchemaWriter
{
    /// <summary>
    /// Creates tables with columns, primary keys, and foreign keys in the target database.
    /// </summary>
    /// <param name="connectionString">Connection string for target database.</param>
    /// <param name="schemaName">Target schema name.</param>
    /// <param name="tables">List of table schemas to create.</param>
    Task CreateSchemaAsync(string connectionString, string schemaName, List<TableSchema> tables);

    /// <summary>
    /// Creates views in the target database with converted SQL definitions.
    /// </summary>
    /// <param name="connectionString">Connection string for target database.</param>
    /// <param name="schemaName">Target schema name.</param>
    /// <param name="views">List of view schemas to create.</param>
    /// <param name="sourceTables">Source tables for building name mappings in view definitions.</param>
    Task CreateViewsAsync(string connectionString, string schemaName, List<ViewSchema> views,
        List<TableSchema>? sourceTables = null);

    /// <summary>
    /// Creates indexes on tables in the target database.
    /// </summary>
    /// <param name="connectionString">Connection string for target database.</param>
    /// <param name="schemaName">Target schema name.</param>
    /// <param name="indexes">List of index schemas to create.</param>
    Task CreateIndexesAsync(string connectionString, string schemaName, List<IndexSchema> indexes);

    /// <summary>
    /// Creates constraints (check, unique, default) on tables in the target database.
    /// </summary>
    /// <param name="connectionString">Connection string for target database.</param>
    /// <param name="schemaName">Target schema name.</param>
    /// <param name="constraints">List of constraint schemas to create.</param>
    Task CreateConstraintsAsync(string connectionString, string schemaName, List<ConstraintSchema> constraints);
}