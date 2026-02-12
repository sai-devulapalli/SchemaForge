using SchemaForge.Services.Interfaces;
using Npgsql;
using SchemaForge.Models;
using System.Data;
using Microsoft.Extensions.Logging;

namespace SchemaForge.Services.DataWriter;

/// <summary>
/// Writes data to PostgreSQL databases.
/// Uses COPY binary protocol for high-performance bulk inserts.
/// Each operation creates its own connection (per-batch connection pattern).
/// </summary>
public class PostgresDataWriter(INamingConverter namingConverter, ILogger<PostgresDataWriter> logger) : IDataWriter
{
    /// <summary>
    /// Performs a bulk insert using PostgreSQL's COPY binary protocol.
    /// Wraps the operation in a transaction for atomicity.
    /// </summary>
    public async Task BulkInsertAsync(
        string connectionString,
        string schemaName,
        TableSchema table,
        DataTable dataTable)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        // Set session_replication_role on this connection to bypass constraints
        await using (var setCmd = new NpgsqlCommand("SET session_replication_role = 'replica';", connection))
        {
            await setCmd.ExecuteNonQueryAsync();
        }

        // Fix #4: Wrap bulk insert in a transaction for atomicity and rollback capability
        await using var transaction = await connection.BeginTransactionAsync();
        try
        {
            var tableName = namingConverter.Convert(table.TableName);
            var fullTableName = $"{schemaName}.\"{tableName}\"";
            var columns = table.Columns.Select(c => $"\"{namingConverter.Convert(c.ColumnName)}\"");
            var columnList = string.Join(", ", columns);

            // Check if the table has existing rows and truncate if necessary
            if (await TableHasRowsAsync(connection, schemaName, tableName))
            {
                logger.LogInformation("Truncating table {Schema}.{Table} before bulk insert.", schemaName, tableName);
                await using var truncateCmd = new NpgsqlCommand($"TRUNCATE TABLE {fullTableName} RESTART IDENTITY CASCADE;", connection);
                await truncateCmd.ExecuteNonQueryAsync();
            }

            await using var writer = await connection.BeginBinaryImportAsync(
                $"COPY {fullTableName} ({columnList}) FROM STDIN (FORMAT BINARY)");

            await using (writer)
            {
                foreach (DataRow row in dataTable.Rows)
                {
                    await writer.StartRowAsync();

                    foreach (var column in table.Columns)
                    {
                        var value = row[column.ColumnName];

                        if (value == DBNull.Value)
                        {
                            await writer.WriteNullAsync();
                        }
                        else
                        {
                            await writer.WriteAsync(
                                ConvertValue(value, column),
                                GetNpgsqlDbType(column));
                        }
                    }
                }
                await writer.CompleteAsync();
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
    /// Resets PostgreSQL sequences to continue from the maximum value in the table.
    /// Uses pg_get_serial_sequence() to find actual sequence names instead of convention-based guessing.
    /// </summary>
    public async Task ResetSequencesAsync(
        string connectionString,
        string schemaName,
        TableSchema table)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        var identityColumns = table.Columns.Where(c => c.IsIdentity);

        foreach (var column in identityColumns)
        {
            var tableName = namingConverter.Convert(table.TableName);
            var columnName = namingConverter.Convert(column.ColumnName);
            var fullTableName = $"{schemaName}.\"{tableName}\"";

            // Fix #11: Use pg_get_serial_sequence to find actual sequence name
            // instead of assuming convention-based naming
            var findSeqSql = $"""
                SELECT pg_get_serial_sequence('{schemaName}.{tableName}', '{columnName}')
                """;

            try
            {
                string? sequenceName;
                await using (var findCmd = new NpgsqlCommand(findSeqSql, connection))
                {
                    var result = await findCmd.ExecuteScalarAsync();
                    sequenceName = result as string;
                }

                // Fallback to convention-based name if pg_get_serial_sequence returns null
                if (string.IsNullOrEmpty(sequenceName))
                {
                    sequenceName = $"{schemaName}.\"{tableName}_{columnName}_seq\"";
                    logger.LogWarning("Could not find sequence via pg_get_serial_sequence for {Table}.{Column}, falling back to convention: {Sequence}",
                        tableName, columnName, sequenceName);
                }

                var resetSql = $"""
                    SELECT setval('{sequenceName}',
                        COALESCE((SELECT MAX("{columnName}") FROM {fullTableName}), 1),
                        true);
                    """;

                await using var cmd = new NpgsqlCommand(resetSql, connection);
                await cmd.ExecuteNonQueryAsync();
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to reset sequence for {Table}.{Column}", tableName, columnName);
            }
        }
    }

    /// <summary>
    /// Disables foreign key constraints by setting session replication role to 'replica'.
    /// Note: This is session-level and only affects the connection it is set on.
    /// Each BulkInsertAsync call now sets this on its own connection.
    /// </summary>
    public async Task DisableConstraintsAsync(string connectionString)
    {
        // Fix #14: Log connection target (without credentials)
        LogConnectionTarget(connectionString, "Disabling constraints");

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var cmd = new NpgsqlCommand("SET session_replication_role = 'replica';", connection);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Re-enables foreign key constraints by restoring session replication role to 'origin'.
    /// </summary>
    public async Task EnableConstraintsAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var cmd = new NpgsqlCommand("SET session_replication_role = 'origin';", connection);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Logs the connection target (host/database) without exposing credentials.
    /// </summary>
    private void LogConnectionTarget(string connectionString, string operation)
    {
        try
        {
            var builder = new NpgsqlConnectionStringBuilder(connectionString);
            logger.LogInformation("{Operation} on {Host}/{Database}", operation, builder.Host, builder.Database);
        }
        catch
        {
            logger.LogInformation("{Operation} on target database", operation);
        }
    }

    /// <summary>
    /// Converts source values to PostgreSQL-compatible types.
    /// Handles cross-database type mismatches (e.g., MySQL tinyint(1) → Boolean).
    /// </summary>
    private static object ConvertValue(object value, ColumnSchema column)
    {
        if (value == DBNull.Value) return DBNull.Value;

        var targetType = GetNpgsqlDbType(column);

        // Handle MySQL tinyint(1) which returns System.Boolean but maps to Smallint
        if (value is bool boolVal)
        {
            if (targetType == NpgsqlTypes.NpgsqlDbType.Smallint || targetType == NpgsqlTypes.NpgsqlDbType.Integer)
                return (short)(boolVal ? 1 : 0);
            return boolVal;
        }

        // Handle Oracle NUMBER → integer conversion (Oracle returns decimal for all NUMBER types)
        if (value is decimal decimalVal)
        {
            return targetType switch
            {
                NpgsqlTypes.NpgsqlDbType.Integer => (int)decimalVal,
                NpgsqlTypes.NpgsqlDbType.Bigint => (long)decimalVal,
                NpgsqlTypes.NpgsqlDbType.Smallint => (short)decimalVal,
                NpgsqlTypes.NpgsqlDbType.Double => (double)decimalVal,
                NpgsqlTypes.NpgsqlDbType.Real => (float)decimalVal,
                _ => decimalVal
            };
        }

        return column.DataType.ToLowerInvariant() switch
        {
            "bit" => Convert.ToBoolean(value),
            "uniqueidentifier" => Guid.Parse(value.ToString()!),
            _ => value
        };
    }

    /// <summary>
    /// Maps source data types to NpgsqlDbType for the COPY protocol.
    /// </summary>
    private static NpgsqlTypes.NpgsqlDbType GetNpgsqlDbType(ColumnSchema column)
    {
        var type = column.DataType.ToLowerInvariant();

        // Normalize Oracle TIMESTAMP(n) variants
        if (type.StartsWith("timestamp"))
        {
            if (type.Contains("with time zone"))
                return NpgsqlTypes.NpgsqlDbType.TimestampTz;
            return NpgsqlTypes.NpgsqlDbType.Timestamp;
        }

        return type switch
        {
            "int" or "integer" => NpgsqlTypes.NpgsqlDbType.Integer,
            "bigint" => NpgsqlTypes.NpgsqlDbType.Bigint,
            "smallint" => NpgsqlTypes.NpgsqlDbType.Smallint,
            "tinyint" => NpgsqlTypes.NpgsqlDbType.Smallint,
            "bit" or "boolean" => NpgsqlTypes.NpgsqlDbType.Boolean,
            "decimal" or "numeric" or "money" or "smallmoney" => NpgsqlTypes.NpgsqlDbType.Numeric,
            "number" => column.Scale.HasValue && column.Scale > 0
                ? NpgsqlTypes.NpgsqlDbType.Numeric
                : NpgsqlTypes.NpgsqlDbType.Integer,
            "float" or "double" or "double precision" or "binary_double" => NpgsqlTypes.NpgsqlDbType.Double,
            "real" or "binary_float" => NpgsqlTypes.NpgsqlDbType.Real,
            "datetime" or "datetime2" or "smalldatetime" => NpgsqlTypes.NpgsqlDbType.Timestamp,
            "date" => NpgsqlTypes.NpgsqlDbType.Date,
            "time" or "time without time zone" => NpgsqlTypes.NpgsqlDbType.Time,
            "datetimeoffset" => NpgsqlTypes.NpgsqlDbType.TimestampTz,
            "uniqueidentifier" or "uuid" => NpgsqlTypes.NpgsqlDbType.Uuid,
            "varbinary" or "binary" or "image" or "bytea" or "blob" or "raw" => NpgsqlTypes.NpgsqlDbType.Bytea,
            "xml" or "xmltype" => NpgsqlTypes.NpgsqlDbType.Xml,
            "json" => NpgsqlTypes.NpgsqlDbType.Json,
            "jsonb" => NpgsqlTypes.NpgsqlDbType.Jsonb,
            _ => NpgsqlTypes.NpgsqlDbType.Text
        };
    }

    private async Task<bool> TableHasRowsAsync(NpgsqlConnection connection, string schemaName, string tableName)
    {
        var sql = $"SELECT EXISTS (SELECT 1 FROM {schemaName}.\"{tableName}\" LIMIT 1)";
        await using var cmd = new NpgsqlCommand(sql, connection);
        return Convert.ToBoolean(await cmd.ExecuteScalarAsync());
    }
}
