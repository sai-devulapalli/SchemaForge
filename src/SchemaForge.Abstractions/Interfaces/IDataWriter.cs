using System.Data;
using SchemaForge.Abstractions.Models;

namespace SchemaForge.Abstractions.Interfaces;

/// <summary>
/// Interface for writing data to a target database.
/// Provides bulk insert capabilities and constraint management.
/// </summary>
public interface IDataWriter
{
    /// <summary>
    /// Performs a bulk insert of data into the target table.
    /// </summary>
    Task BulkInsertAsync(string connectionString, string schemaName, TableSchema table, DataTable dataTable);

    /// <summary>
    /// Resets identity/sequence values after data import to continue from max value.
    /// </summary>
    Task ResetSequencesAsync(string connectionString, string schemaName, TableSchema table);

    /// <summary>
    /// Disables foreign key constraints to allow data import in any order.
    /// </summary>
    Task DisableConstraintsAsync(string connectionString);

    /// <summary>
    /// Re-enables foreign key constraints after data import is complete.
    /// </summary>
    Task EnableConstraintsAsync(string connectionString);
}