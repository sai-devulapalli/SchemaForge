using SchemaForge.Models;

namespace SchemaForge.Services.Interfaces;

/// <summary>
/// Interface for migrating data between databases.
/// Handles bulk data transfer with batching and constraint management.
/// </summary>
public interface IDataMigrator
{
    /// <summary>
    /// Migrates data from source database to target database for specified tables.
    /// </summary>
    /// <param name="sourceDatabaseType">Source database type key (e.g., "sqlserver", "postgres").</param>
    /// <param name="targetDatabaseType">Target database type key (e.g., "sqlserver", "postgres").</param>
    /// <param name="sourceConnectionString">Connection string for source database.</param>
    /// <param name="targetConnectionString">Connection string for target database.</param>
    /// <param name="targetSchemaName">Schema name in target database.</param>
    /// <param name="tables">List of tables to migrate data for.</param>
    /// <param name="batchSize">Number of rows to process per batch.</param>
    Task MigrateDataAsync(
        string sourceDatabaseType,
        string targetDatabaseType,
        string sourceConnectionString,
        string targetConnectionString,
        string targetSchemaName,
        List<TableSchema> tables,
        int batchSize);
}