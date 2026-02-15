using SchemaForge.Abstractions.Models;

namespace SchemaForge.Abstractions.Interfaces;

/// <summary>
/// Interface for migrating data between databases.
/// Handles bulk data transfer with batching and constraint management.
/// </summary>
public interface IDataMigrator
{
    /// <summary>
    /// Migrates data from source database to target database for specified tables.
    /// </summary>
    Task MigrateDataAsync(
        string sourceDatabaseType,
        string targetDatabaseType,
        string sourceConnectionString,
        string targetConnectionString,
        string targetSchemaName,
        List<TableSchema> tables,
        int batchSize);
}