using SchemaForge.Services.Interfaces;
using Oracle.ManagedDataAccess.Client;
using SchemaForge.Models;
using System.Data;
using Microsoft.Extensions.Logging;

namespace SchemaForge.Services.DataWriter;

/// <summary>
/// Writes data to Oracle databases.
/// Uses parameterized INSERT statements for data migration.
/// Each operation creates its own connection (per-batch connection pattern).
/// </summary>
public class OracleDataWriter(INamingConverter namingConverter, ILogger<OracleDataWriter> logger) : IDataWriter
{
    /// <summary>
    /// Performs bulk insert using parameterized INSERT statements within a transaction.
    /// </summary>
    public async Task BulkInsertAsync(
        string connectionString,
        string schemaName,
        TableSchema table,
        DataTable dataTable)
    {
        await using var connection = new OracleConnection(connectionString);
        await connection.OpenAsync();

        // Fix #4: Wrap inserts in a transaction for atomicity and rollback capability
        await using var transaction = await connection.BeginTransactionAsync();
        try
        {
            var tableName = namingConverter.Convert(table.TableName);
            var columns = table.Columns.Select(c => namingConverter.Convert(c.ColumnName));
            var columnList = string.Join(", ", columns);

            foreach (DataRow row in dataTable.Rows)
            {
                var values = new List<string>();
                var parameters = new List<OracleParameter>();

                for (int i = 0; i < table.Columns.Count; i++)
                {
                    var column = table.Columns[i];
                    var value = row[column.ColumnName];

                    var paramName = $":p{i}";
                    values.Add(paramName);

                    var param = new OracleParameter(paramName, value == DBNull.Value ? DBNull.Value : ConvertToOracleType(value, column));
                    parameters.Add(param);
                }

                var sql = $"INSERT INTO {schemaName}.{tableName} ({columnList}) VALUES ({string.Join(", ", values)})";

                await using var cmd = new OracleCommand(sql, connection);
                cmd.Transaction = (OracleTransaction)transaction;
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
    /// Resets Oracle sequences to continue from the maximum value in the table.
    /// Drops and recreates sequences with the new start value.
    /// </summary>
    public async Task ResetSequencesAsync(
        string connectionString,
        string schemaName,
        TableSchema table)
    {
        await using var connection = new OracleConnection(connectionString);
        await connection.OpenAsync();

        var identityColumns = table.Columns.Where(c => c.IsIdentity);

        foreach (var column in identityColumns)
        {
            var tableName = namingConverter.Convert(table.TableName);
            var columnName = namingConverter.Convert(column.ColumnName);
            var sequenceName = $"{tableName}_{columnName}_seq";

            var sql = $"""
                DECLARE
                    max_val NUMBER;
                BEGIN
                    SELECT NVL(MAX({columnName}), 0) + 1 INTO max_val FROM {schemaName}.{tableName};
                    EXECUTE IMMEDIATE 'DROP SEQUENCE {schemaName}.{sequenceName}';
                    EXECUTE IMMEDIATE 'CREATE SEQUENCE {schemaName}.{sequenceName} START WITH ' || max_val;
                EXCEPTION
                    WHEN OTHERS THEN
                        NULL;
                END;
                """;

            try
            {
                await using var cmd = new OracleCommand(sql, connection);
                await cmd.ExecuteNonQueryAsync();
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to reset sequence for {Table}.{Column}", tableName, columnName);
            }
        }
    }

    /// <summary>
    /// Disables all referential (foreign key) constraints for the current user.
    /// Uses PL/SQL to iterate through and disable each constraint.
    /// </summary>
    public async Task DisableConstraintsAsync(string connectionString)
    {
        // Fix #14: Log connection target (without credentials)
        LogConnectionTarget(connectionString, "Disabling constraints");

        await using var connection = new OracleConnection(connectionString);
        await connection.OpenAsync();

        var sql = """
            BEGIN
                FOR c IN (SELECT table_name, constraint_name FROM user_constraints WHERE constraint_type = 'R') LOOP
                    EXECUTE IMMEDIATE 'ALTER TABLE ' || c.table_name || ' DISABLE CONSTRAINT ' || c.constraint_name;
                END LOOP;
            END;
            """;

        await using var cmd = new OracleCommand(sql, connection);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Re-enables all referential (foreign key) constraints for the current user.
    /// </summary>
    public async Task EnableConstraintsAsync(string connectionString)
    {
        await using var connection = new OracleConnection(connectionString);
        await connection.OpenAsync();

        var sql = """
            BEGIN
                FOR c IN (SELECT table_name, constraint_name FROM user_constraints WHERE constraint_type = 'R') LOOP
                    EXECUTE IMMEDIATE 'ALTER TABLE ' || c.table_name || ' ENABLE CONSTRAINT ' || c.constraint_name;
                END LOOP;
            END;
            """;

        await using var cmd = new OracleCommand(sql, connection);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Logs the connection target (host/database) without exposing credentials.
    /// </summary>
    private void LogConnectionTarget(string connectionString, string operation)
    {
        try
        {
            var builder = new OracleConnectionStringBuilder(connectionString);
            logger.LogInformation("{Operation} on {DataSource}", operation, builder.DataSource);
        }
        catch
        {
            logger.LogInformation("{Operation} on target database", operation);
        }
    }

    /// <summary>
    /// Converts source values to Oracle-compatible types.
    /// Handles boolean to number and GUID to byte array conversions.
    /// </summary>
    private static object ConvertToOracleType(object value, ColumnSchema column)
    {
        if (value == DBNull.Value) return DBNull.Value;

        // Handle by CLR type first for types that need conversion regardless of source column type
        switch (value)
        {
            case bool boolVal:
                return boolVal ? 1 : 0;
            case DateOnly d:
                return d.ToDateTime(TimeOnly.MinValue);
            case TimeOnly t:
                return DateTime.MinValue.Add(t.ToTimeSpan());
            case DateTimeOffset dto:
                return dto;
            case Guid g:
                return g.ToByteArray();
        }

        return column.DataType.ToLowerInvariant() switch
        {
            "bit" or "boolean" => Convert.ToBoolean(value) ? 1 : 0,
            "uniqueidentifier" or "uuid" => Guid.Parse(value.ToString()!).ToByteArray(),
            _ => value
        };
    }
}
