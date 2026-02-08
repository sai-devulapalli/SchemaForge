using System.Data;
using SchemaForge.Models;

namespace SchemaForge.Services.Interfaces;

/// <summary>
/// Interface for reading data from a source database.
/// Provides methods to count rows and fetch data in batches.
/// </summary>
public interface IDataReader
{
    /// <summary>
    /// Gets the total number of rows in the specified table.
    /// </summary>
    /// <param name="connectionString">Connection string for the database.</param>
    /// <param name="table">Table schema containing schema and table name.</param>
    /// <returns>Total row count.</returns>
    Task<int> GetRowCountAsync(string connectionString, TableSchema table);

    /// <summary>
    /// Fetches a batch of rows from the specified table.
    /// </summary>
    /// <param name="connectionString">Connection string for the database.</param>
    /// <param name="table">Table schema containing schema and table name.</param>
    /// <param name="offset">Number of rows to skip (for pagination).</param>
    /// <param name="batchSize">Maximum number of rows to return.</param>
    /// <returns>DataTable containing the fetched rows.</returns>
    Task<DataTable> FetchBatchAsync(string connectionString, TableSchema table, int offset, int batchSize);
}
