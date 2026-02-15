using System.Data;
using SchemaForge.Abstractions.Models;

namespace SchemaForge.Abstractions.Interfaces;

/// <summary>
/// Interface for reading data from a source database.
/// Provides methods to count rows and fetch data in batches.
/// </summary>
public interface IDataReader
{
    /// <summary>
    /// Gets the total number of rows in the specified table.
    /// </summary>
    Task<int> GetRowCountAsync(string connectionString, TableSchema table);

    /// <summary>
    /// Fetches a batch of rows from the specified table.
    /// </summary>
    Task<DataTable> FetchBatchAsync(string connectionString, TableSchema table, int offset, int batchSize);
}