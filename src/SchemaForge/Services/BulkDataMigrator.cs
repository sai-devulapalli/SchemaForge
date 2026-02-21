using System.Data;
using System.Text;
using SchemaForge.Abstractions.Interfaces;
using SchemaForge.Abstractions.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using IDataReader = SchemaForge.Abstractions.Interfaces.IDataReader;

namespace SchemaForge.Services;

/// <summary>
/// Handles bulk data migration between databases.
/// Reads data in batches from source, writes to target using bulk operations.
/// Manages constraint disabling/enabling and sequence reset.
/// Uses keyed DI to resolve database-specific IDataReader/IDataWriter implementations,
/// eliminating the need for type-checking switches (Open/Closed Principle).
/// </summary>
public class BulkDataMigrator(
    ILogger<BulkDataMigrator> logger,
    INamingConverter namingConverter,
    ISqlCollector sqlCollector,
    IServiceProvider serviceProvider) : IDataMigrator
{
    /// <summary>
    /// Migrates data from source to target database for all specified tables.
    /// Disables constraints before migration and re-enables them after.
    /// Uses try/finally to ensure constraints are always re-enabled even on failure.
    /// In dry run mode, generates sample INSERT statements.
    /// </summary>
    public async Task MigrateDataAsync(string sourceDatabaseType, string targetDatabaseType,
                                        string sourceConnectionString, string targetConnectionString,
                                        string targetSchemaName, List<TableSchema> tables, int batchSize)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceDatabaseType, nameof(sourceDatabaseType));
        ArgumentException.ThrowIfNullOrWhiteSpace(targetDatabaseType, nameof(targetDatabaseType));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(batchSize, nameof(batchSize));

        logger.LogInformation("Starting data migration...");

        var dataReader = serviceProvider.GetRequiredKeyedService<IDataReader>(sourceDatabaseType);

        if (sqlCollector.IsCollecting)
        {
            sqlCollector.AddComment("Data Migration (sample INSERT statements)");
            await GenerateDataSampleSqlAsync(dataReader, sourceConnectionString, targetSchemaName, tables, batchSize);
            logger.LogInformation("Data migration samples generated");
            return;
        }

        var dataWriter = serviceProvider.GetRequiredKeyedService<IDataWriter>(targetDatabaseType);

        // Fix #6: Wrap constraint disable/enable in try/finally for exception safety
        await dataWriter.DisableConstraintsAsync(targetConnectionString);
        var failedTables = new List<string>();
        try
        {
            foreach (var table in tables)
            {
                try
                {
                    await MigrateTableDataAsync(
                        dataReader,
                        dataWriter,
                        sourceConnectionString,
                        targetConnectionString,
                        targetSchemaName,
                        table,
                        batchSize);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to migrate data for table {Table}. Continuing with remaining tables.", table.TableName);
                    failedTables.Add(table.TableName);
                }
            }

            if (failedTables.Count > 0)
            {
                throw new AggregateException(
                    $"Data migration failed for {failedTables.Count} table(s): {string.Join(", ", failedTables)}");
            }
        }
        finally
        {
            // Always re-enable constraints, even if migration fails
            try
            {
                await dataWriter.EnableConstraintsAsync(targetConnectionString);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to re-enable constraints after migration. Manual intervention may be required.");
            }
        }

        logger.LogInformation("Data migration completed");
    }

    /// <summary>
    /// Migrates data for a single table in batches.
    /// Logs progress as batches are processed.
    /// </summary>
    private async Task MigrateTableDataAsync(IDataReader dataReader, IDataWriter dataWriter,
                                                string sourceConnectionString, string targetConnectionString,
                                                string targetSchemaName, TableSchema table, int batchSize)
    {
        logger.LogInformation("Migrating table: {Table}", table.TableName);

        var totalCount = await dataReader.GetRowCountAsync(sourceConnectionString, table);
        logger.LogInformation("  Total records: {Count}", totalCount);

        if (totalCount == 0) return;

        var offset = 0;
        while (offset < totalCount)
        {
            var dataTable = await dataReader.FetchBatchAsync(sourceConnectionString, table, offset, batchSize);

            if (dataTable.Rows.Count > 0)
            {
                await dataWriter.BulkInsertAsync(targetConnectionString, targetSchemaName, table, dataTable);
                offset += dataTable.Rows.Count;
                logger.LogInformation("  Migrated {Current}/{Total} records", offset, totalCount);
            }
            else
            {
                break;
            }
        }

        await dataWriter.ResetSequencesAsync(targetConnectionString, targetSchemaName, table);
    }

    /// <summary>
    /// Generates sample INSERT statements for dry run mode.
    /// </summary>
    private async Task GenerateDataSampleSqlAsync(
        IDataReader dataReader,
        string sourceConnectionString,
        string targetSchemaName,
        List<TableSchema> tables,
        int sampleCount)
    {
        foreach (var table in tables)
        {
            var tableName = namingConverter.Convert(table.TableName);
            var fullTableName = $"{targetSchemaName}.\"{tableName}\"";

            var totalCount = await dataReader.GetRowCountAsync(sourceConnectionString, table);

            if (totalCount == 0)
            {
                sqlCollector.AddComment($"Table {tableName}: No data (0 rows)");
                continue;
            }

            sqlCollector.AddComment($"Table {tableName}: {sampleCount} sample rows (total: {totalCount} rows)");

            var dataTable = await dataReader.FetchBatchAsync(sourceConnectionString, table, 0, sampleCount);

            foreach (DataRow row in dataTable.Rows)
            {
                var columns = string.Join(", ",
                    table.Columns.Select(c => $"\"{namingConverter.Convert(c.ColumnName)}\""));
                var values = string.Join(", ",
                    table.Columns.Select(c => FormatValue(row[c.ColumnName], c)));

                var sql = $"INSERT INTO {fullTableName} ({columns}) VALUES ({values})";
                sqlCollector.AddSql(sql, "Data", tableName);
            }
        }
    }

    /// <summary>
    /// Formats a value for SQL INSERT statement.
    /// </summary>
    private static string FormatValue(object? value, ColumnSchema column)
    {
        if (value is null or DBNull)
            return "NULL";

        var dataType = column.DataType.ToLowerInvariant();

        // Numeric types
        if (dataType.Contains("int") || dataType.Contains("decimal") ||
            dataType.Contains("numeric") || dataType.Contains("float") ||
            dataType.Contains("double") || dataType.Contains("real") ||
            dataType.Contains("money") || dataType == "number")
        {
            return value.ToString() ?? "NULL";
        }

        // Boolean types
        if (dataType is "bit" or "boolean" or "bool")
        {
            return Convert.ToBoolean(value) ? "TRUE" : "FALSE";
        }

        // Date/time types
        if (dataType.Contains("date") || dataType.Contains("time") || dataType.Contains("timestamp"))
        {
            return $"'{Convert.ToDateTime(value):yyyy-MM-dd HH:mm:ss}'";
        }

        // Binary types - output as hex
        if (dataType.Contains("binary") || dataType.Contains("blob") ||
            dataType is "bytea" or "raw" or "image")
        {
            if (value is byte[] bytes)
            {
                return $"0x{BitConverter.ToString(bytes).Replace("-", "")}";
            }
        }

        // GUID types
        if (dataType is "uniqueidentifier" or "uuid")
        {
            return $"'{value}'";
        }

        // String types (default)
        var stringValue = (value.ToString() ?? string.Empty).Replace("'", "''");
        return $"'{stringValue}'";
    }
}
