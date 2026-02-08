using SchemaForge.Services.Interfaces;
using MySql.Data.MySqlClient;
using SchemaForge.Models;
using System.Data;
using Microsoft.Extensions.Logging;

namespace SchemaForge.Services.DataWriter;

/// <summary>
/// Writes data to MySQL databases.
/// Uses parameterized INSERT statements for data migration.
/// Each operation creates its own connection (per-batch connection pattern).
/// </summary>
public class MySqlDataWriter(INamingConverter namingConverter, ILogger<MySqlDataWriter> logger) : IDataWriter
{
    /// <summary>
    /// Performs bulk insert using parameterized INSERT statements within a transaction.
    /// Sets FOREIGN_KEY_CHECKS=0 per-connection to bypass constraints.
    /// </summary>
    public async Task BulkInsertAsync(
        string connectionString,
        string schemaName,
        TableSchema table,
        DataTable dataTable)
    {
        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync();

        // Disable FK checks on this connection
        await using (var fkCmd = new MySqlCommand("SET FOREIGN_KEY_CHECKS=0;", connection))
        {
            await fkCmd.ExecuteNonQueryAsync();
        }

        // Fix #4: Wrap inserts in a transaction for atomicity and rollback capability
        await using var transaction = await connection.BeginTransactionAsync();
        try
        {
            var tableName = namingConverter.Convert(table.TableName);
            var columns = table.Columns.Select(c => $"`{namingConverter.Convert(c.ColumnName)}`");
            var columnList = string.Join(", ", columns);

            foreach (DataRow row in dataTable.Rows)
            {
                var values = new List<string>();
                var parameters = new List<MySqlParameter>();

                for (int i = 0; i < table.Columns.Count; i++)
                {
                    var column = table.Columns[i];
                    var value = row[column.ColumnName];

                    var paramName = $"@p{i}";
                    values.Add(paramName);
                    parameters.Add(new MySqlParameter(paramName, value == DBNull.Value ? null : value));
                }

                var sql = $"INSERT INTO `{tableName}` ({columnList}) VALUES ({string.Join(", ", values)})";

                await using var cmd = new MySqlCommand(sql, connection);
                cmd.Transaction = (MySqlTransaction)transaction;
                cmd.Parameters.AddRange(parameters.ToArray());
                await cmd.ExecuteNonQueryAsync();
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
    /// Resets MySQL AUTO_INCREMENT to continue from the maximum value.
    /// </summary>
    public async Task ResetSequencesAsync(
        string connectionString,
        string schemaName,
        TableSchema table)
    {
        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync();

        var identityColumns = table.Columns.Where(c => c.IsIdentity);

        foreach (var column in identityColumns)
        {
            var tableName = namingConverter.Convert(table.TableName);
            var columnName = namingConverter.Convert(column.ColumnName);

            try
            {
                // First get the max value
                var maxSql = $"SELECT COALESCE(MAX(`{columnName}`), 0) + 1 FROM `{tableName}`";
                await using var maxCmd = new MySqlCommand(maxSql, connection);
                var maxVal = Convert.ToInt64(await maxCmd.ExecuteScalarAsync());

                // Then set the AUTO_INCREMENT
                var alterSql = $"ALTER TABLE `{tableName}` AUTO_INCREMENT = {maxVal}";
                await using var alterCmd = new MySqlCommand(alterSql, connection);
                await alterCmd.ExecuteNonQueryAsync();
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to reset AUTO_INCREMENT for {Table}.{Column}", tableName, columnName);
            }
        }
    }

    /// <summary>
    /// Disables foreign key checks to allow data insertion in any order.
    /// </summary>
    public async Task DisableConstraintsAsync(string connectionString)
    {
        // Fix #14: Log connection target (without credentials)
        LogConnectionTarget(connectionString, "Disabling constraints");

        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync();
        await using var cmd = new MySqlCommand("SET FOREIGN_KEY_CHECKS=0;", connection);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Re-enables foreign key checks after data migration is complete.
    /// </summary>
    public async Task EnableConstraintsAsync(string connectionString)
    {
        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync();
        await using var cmd = new MySqlCommand("SET FOREIGN_KEY_CHECKS=1;", connection);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Logs the connection target (host/database) without exposing credentials.
    /// </summary>
    private void LogConnectionTarget(string connectionString, string operation)
    {
        try
        {
            var builder = new MySqlConnectionStringBuilder(connectionString);
            logger.LogInformation("{Operation} on {Server}/{Database}", operation, builder.Server, builder.Database);
        }
        catch
        {
            logger.LogInformation("{Operation} on target database", operation);
        }
    }
}
