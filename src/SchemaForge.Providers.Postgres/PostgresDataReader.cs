using Npgsql;
using SchemaForge.Abstractions.Models;
using SchemaForge.Abstractions.Interfaces;
using System.Data;
using System.Text.RegularExpressions;
using IDataReader = SchemaForge.Abstractions.Interfaces.IDataReader;

namespace SchemaForge.Providers.Postgres;

/// <summary>
/// Reads data from PostgreSQL databases.
/// Uses LIMIT/OFFSET for pagination during batch data extraction.
/// Validates identifiers to prevent SQL injection.
/// </summary>
public class PostgresDataReader : IDataReader
{
    /// <summary>
    /// Validates and quotes a PostgreSQL identifier to prevent SQL injection.
    /// </summary>
    private static string QuoteIdentifier(string identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier))
            throw new ArgumentException("Identifier cannot be null or empty", nameof(identifier));
        if (!Regex.IsMatch(identifier, @"^[a-zA-Z_][a-zA-Z0-9_ ]*$"))
            throw new ArgumentException($"Invalid PostgreSQL identifier: {identifier}", nameof(identifier));
        return $"\"{identifier}\"";
    }

    /// <summary>
    /// Builds a deterministic ORDER BY clause from primary keys.
    /// Falls back to first column if no primary keys are defined.
    /// </summary>
    private static string BuildOrderByClause(TableSchema table)
    {
        if (table.PrimaryKeys.Count > 0)
        {
            var pkColumns = string.Join(", ", table.PrimaryKeys.Select(pk => QuoteIdentifier(pk)));
            return $"ORDER BY {pkColumns}";
        }
        if (table.Columns.Count > 0)
        {
            return $"ORDER BY {QuoteIdentifier(table.Columns[0].ColumnName)}";
        }
        return "ORDER BY 1";
    }

    /// <summary>
    /// Gets the total row count for a table.
    /// </summary>
    public async Task<int> GetRowCountAsync(string connectionString, TableSchema table)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        var schemaName = QuoteIdentifier(table.SchemaName);
        var tableName = QuoteIdentifier(table.TableName);
        var query = $"SELECT COUNT(*) FROM {schemaName}.{tableName}";
        await using var cmd = new NpgsqlCommand(query, connection);
        var result = await cmd.ExecuteScalarAsync();
        return Convert.ToInt32(result);
    }

    /// <summary>
    /// Fetches a batch of rows from a PostgreSQL table using LIMIT/OFFSET.
    /// Uses primary key ordering for deterministic pagination.
    /// </summary>
    public async Task<DataTable> FetchBatchAsync(
        string connectionString,
        TableSchema table,
        int offset,
        int batchSize)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        var schemaName = QuoteIdentifier(table.SchemaName);
        var tableName = QuoteIdentifier(table.TableName);
        var orderBy = BuildOrderByClause(table);
        var query = $"""
                     SELECT * FROM {schemaName}.{tableName}
                     {orderBy}
                     LIMIT {batchSize} OFFSET {offset}
                     """;

        await using var cmd = new NpgsqlCommand(query, connection);
        await using var reader = await cmd.ExecuteReaderAsync();

        var dataTable = new DataTable();
        dataTable.Load(reader);
        return dataTable;
    }
}
