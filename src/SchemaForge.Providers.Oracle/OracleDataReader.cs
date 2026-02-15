using Oracle.ManagedDataAccess.Client;
using SchemaForge.Abstractions.Models;
using SchemaForge.Abstractions.Interfaces;
using System.Data;
using System.Text.RegularExpressions;
using IDataReader = SchemaForge.Abstractions.Interfaces.IDataReader;

namespace SchemaForge.Providers.Oracle;

/// <summary>
/// Reads data from Oracle databases.
/// Uses OFFSET/FETCH for pagination during batch data extraction.
/// Validates identifiers to prevent SQL injection.
/// </summary>
public class OracleDataReader : IDataReader
{
    /// <summary>
    /// Validates and quotes an Oracle identifier to prevent SQL injection.
    /// </summary>
    private static string QuoteIdentifier(string identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier))
            throw new ArgumentException("Identifier cannot be null or empty", nameof(identifier));
        if (!Regex.IsMatch(identifier, @"^[a-zA-Z_][a-zA-Z0-9_$ ]*$"))
            throw new ArgumentException($"Invalid Oracle identifier: {identifier}", nameof(identifier));
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
        await using var connection = new OracleConnection(connectionString);
        await connection.OpenAsync();

        var schemaName = QuoteIdentifier(table.SchemaName);
        var tableName = QuoteIdentifier(table.TableName);
        var query = $"SELECT COUNT(*) FROM {schemaName}.{tableName}";
        await using var cmd = new OracleCommand(query, connection);
        var result = await cmd.ExecuteScalarAsync();
        return Convert.ToInt32(result);
    }

    /// <summary>
    /// Fetches a batch of rows from an Oracle table using OFFSET/FETCH.
    /// Uses primary key ordering for deterministic pagination.
    /// </summary>
    public async Task<DataTable> FetchBatchAsync(
        string connectionString,
        TableSchema table,
        int offset,
        int batchSize)
    {
        await using var connection = new OracleConnection(connectionString);
        await connection.OpenAsync();

        var schemaName = QuoteIdentifier(table.SchemaName);
        var tableName = QuoteIdentifier(table.TableName);
        var orderBy = BuildOrderByClause(table);
        var query = $"""
                     SELECT * FROM {schemaName}.{tableName}
                     {orderBy}
                     OFFSET {offset} ROWS FETCH NEXT {batchSize} ROWS ONLY
                     """;

        await using var cmd = new OracleCommand(query, connection);
        await using var reader = await cmd.ExecuteReaderAsync();

        var dataTable = new DataTable();
        dataTable.Load(reader);
        return dataTable;
    }
}
