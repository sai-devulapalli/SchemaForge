using System.Data;
using SchemaForge.Models;

namespace SchemaForge.Services.Interfaces;

/// <summary>
/// Interface for writing data to a target database.
/// Provides bulk insert capabilities and constraint management.
/// </summary>
public interface IDataWriter
{
    /// <summary>
    /// Performs a bulk insert of data into the target table.
    /// </summary>
    /// <param name="connectionString">Connection string for the database.</param>
    /// <param name="schemaName">Target schema name.</param>
    /// <param name="table">Table schema with column definitions.</param>
    /// <param name="dataTable">Data to insert.</param>
    Task BulkInsertAsync(string connectionString, string schemaName, TableSchema table, DataTable dataTable);

    /// <summary>
    /// Resets identity/sequence values after data import to continue from max value.
    /// </summary>
    /// <param name="connectionString">Connection string for the database.</param>
    /// <param name="schemaName">Target schema name.</param>
    /// <param name="table">Table schema with identity column information.</param>
    Task ResetSequencesAsync(string connectionString, string schemaName, TableSchema table);

    /// <summary>
    /// Disables foreign key constraints to allow data import in any order.
    /// </summary>
    /// <param name="connectionString">Connection string for the database.</param>
    Task DisableConstraintsAsync(string connectionString);

    /// <summary>
    /// Re-enables foreign key constraints after data import is complete.
    /// </summary>
    /// <param name="connectionString">Connection string for the database.</param>
    Task EnableConstraintsAsync(string connectionString);
}
