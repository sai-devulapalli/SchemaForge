using SchemaForge.Abstractions.Models;

namespace SchemaForge.Abstractions.Interfaces;

/// <summary>
/// Interface for reading database schema information from a source database.
/// Extracts table definitions, columns, keys, indexes, and constraints.
/// </summary>
public interface ISchemaReader
{
    /// <summary>
    /// Reads table schemas from the source database including columns, keys, indexes, and constraints.
    /// </summary>
    /// <param name="connectionString">Connection string for source database.</param>
    /// <param name="includeTables">Optional list of table names to include (null = all tables).</param>
    /// <param name="excludeTables">Optional list of table names to exclude.</param>
    /// <returns>List of table schemas with complete metadata.</returns>
    Task<List<TableSchema>> ReadSchemaAsync(string connectionString, IReadOnlyList<string>? includeTables = null, IReadOnlyList<string>? excludeTables = null);

    /// <summary>
    /// Reads view definitions from the source database.
    /// </summary>
    /// <param name="connectionString">Connection string for source database.</param>
    /// <returns>List of view schemas with SQL definitions.</returns>
    Task<List<ViewSchema>> ReadViewsAsync(string connectionString);
}