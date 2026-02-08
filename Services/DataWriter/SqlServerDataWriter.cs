using SchemaForge.Services.Interfaces;
using Microsoft.Data.SqlClient;
using SchemaForge.Models;
using System.Data;
using Microsoft.Extensions.Logging;

namespace SchemaForge.Services.DataWriter;

/// <summary>
/// Writes data to SQL Server databases.
/// Uses SqlBulkCopy for high-performance bulk inserts.
/// Each operation creates its own connection (per-batch connection pattern).
/// </summary>
public class SqlServerDataWriter(INamingConverter namingConverter, ILogger<SqlServerDataWriter> logger) : IDataWriter
{
    /// <summary>
    /// Performs bulk insert using SqlBulkCopy within a transaction for atomicity.
    /// </summary>
    public async Task BulkInsertAsync(
        string connectionString,
        string schemaName,
        TableSchema table,
        DataTable dataTable)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();

        // Fix #4: Wrap bulk insert in a transaction for atomicity and rollback capability
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();
        try
        {
            var tableName = namingConverter.Convert(table.TableName);
            var destinationTable = $"[{schemaName}].[{tableName}]";

            // Enable IDENTITY_INSERT for tables with identity columns
            var hasIdentity = table.Columns.Any(c => c.IsIdentity);
            if (hasIdentity)
            {
                await using var identCmd = new SqlCommand($"SET IDENTITY_INSERT {destinationTable} ON", connection, transaction);
                await identCmd.ExecuteNonQueryAsync();
            }

            using var bulkCopy = new SqlBulkCopy(connection, SqlBulkCopyOptions.KeepIdentity, transaction);
            bulkCopy.DestinationTableName = destinationTable;
            bulkCopy.BatchSize = 1000;

            // Map columns
            foreach (var column in table.Columns)
            {
                var convertedName = namingConverter.Convert(column.ColumnName);
                bulkCopy.ColumnMappings.Add(column.ColumnName, convertedName);
            }

            await bulkCopy.WriteToServerAsync(dataTable);

            if (hasIdentity)
            {
                await using var identOffCmd = new SqlCommand($"SET IDENTITY_INSERT {destinationTable} OFF", connection, transaction);
                await identOffCmd.ExecuteNonQueryAsync();
            }

            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    /// <summary>
    /// Resets SQL Server identity values using DBCC CHECKIDENT.
    /// </summary>
    public async Task ResetSequencesAsync(
        string connectionString,
        string schemaName,
        TableSchema table)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();

        var identityColumns = table.Columns.Where(c => c.IsIdentity);

        foreach (var column in identityColumns)
        {
            var tableName = namingConverter.Convert(table.TableName);
            var columnName = namingConverter.Convert(column.ColumnName);
            var fullTableName = $"[{schemaName}].[{tableName}]";

            var sql = $"""
                DECLARE @max_id INT;
                SELECT @max_id = ISNULL(MAX([{columnName}]), 0) FROM {fullTableName};
                IF @max_id > 0
                BEGIN
                    DBCC CHECKIDENT ('{schemaName}.{tableName}', RESEED, @max_id);
                END
                """;

            try
            {
                await using var cmd = new SqlCommand(sql, connection);
                await cmd.ExecuteNonQueryAsync();
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to reset identity for {Table}.{Column}", tableName, columnName);
            }
        }
    }

    /// <summary>
    /// Disables all constraints on all tables using sp_MSforeachtable.
    /// </summary>
    public async Task DisableConstraintsAsync(string connectionString)
    {
        // Fix #14: Log connection target (without credentials)
        LogConnectionTarget(connectionString, "Disabling constraints");

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();

        var sql = """
            EXEC sp_MSforeachtable 'ALTER TABLE ? NOCHECK CONSTRAINT ALL'
            """;

        await using var cmd = new SqlCommand(sql, connection);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Re-enables all constraints on all tables using sp_MSforeachtable.
    /// </summary>
    public async Task EnableConstraintsAsync(string connectionString)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();

        var sql = """
            EXEC sp_MSforeachtable 'ALTER TABLE ? CHECK CONSTRAINT ALL'
            """;

        await using var cmd = new SqlCommand(sql, connection);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Logs the connection target (host/database) without exposing credentials.
    /// </summary>
    private void LogConnectionTarget(string connectionString, string operation)
    {
        try
        {
            var builder = new SqlConnectionStringBuilder(connectionString);
            logger.LogInformation("{Operation} on {Server}/{Database}", operation, builder.DataSource, builder.InitialCatalog);
        }
        catch
        {
            logger.LogInformation("{Operation} on target database", operation);
        }
    }
}
