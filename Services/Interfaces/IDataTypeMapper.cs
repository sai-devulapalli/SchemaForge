using SchemaForge.Models;

namespace SchemaForge.Services.Interfaces;

/// <summary>
/// Interface for mapping data types between different database systems.
/// Converts source database types to equivalent types in the target database.
/// </summary>
public interface IDataTypeMapper
{
    /// <summary>
    /// Maps a column's data type to the equivalent type in the target database.
    /// </summary>
    /// <param name="column">Column schema containing source data type and properties.</param>
    /// <param name="targetDatabase">Target database type (e.g., "postgres", "mysql").</param>
    /// <returns>SQL data type string for the target database.</returns>
    string MapDataType(ColumnSchema column, string targetDatabase);
}