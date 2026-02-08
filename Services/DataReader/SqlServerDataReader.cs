using Microsoft.Data.SqlClient;
using SchemaForge.Models;
using SchemaForge.Services.Interfaces;
using System.Data;
using System.Text.RegularExpressions;
using IDataReader = SchemaForge.Services.Interfaces.IDataReader;

namespace SchemaForge.Services.DataReader;

/// <summary>
/// Reads data from SQL Server databases.
/// Uses OFFSET/FETCH for pagination during batch data extraction.
/// Validates identifiers to prevent SQL injection.
/// </summary>
public class SqlServerDataReader : IDataReader
{
    /// <summary>
    /// Validates and quotes a SQL Server identifier to prevent SQL injection.
    /// </summary>
    private static string QuoteIdentifier(string identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier))
            throw new ArgumentException("Identifier cannot be null or empty", nameof(identifier));
        // Reject identifiers with brackets or other dangerous characters
        if (!Regex.IsMatch(identifier, @"^[a-zA-Z_@#][a-zA-Z0-9_@#$ ]*$"))
            throw new ArgumentException($"Invalid SQL Server identifier: {identifier}", nameof(identifier));
        return $"[{identifier}]";
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
        return "ORDER BY (SELECT NULL)";
    }

    /// <summary>
    /// Gets the total row count for a table.
    /// </summary>
    public async Task<int> GetRowCountAsync(string connectionString, TableSchema table)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();

        var schemaName = QuoteIdentifier(table.SchemaName);
        var tableName = QuoteIdentifier(table.TableName);
        var query = $"SELECT COUNT(*) FROM {schemaName}.{tableName}";
        await using var cmd = new SqlCommand(query, connection);
        return (int)await cmd.ExecuteScalarAsync();
    }

    /// <summary>
    /// Fetches a batch of rows from a SQL Server table using OFFSET/FETCH.
    /// Uses primary key ordering for deterministic pagination.
    /// </summary>
    public async Task<DataTable> FetchBatchAsync(
        string connectionString,
        TableSchema table,
        int offset,
        int batchSize)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();

        var schemaName = QuoteIdentifier(table.SchemaName);
        var tableName = QuoteIdentifier(table.TableName);
        var orderBy = BuildOrderByClause(table);
        var query = $"""
                     SELECT * FROM {schemaName}.{tableName}
                     {orderBy}
                     OFFSET {offset} ROWS
                     FETCH NEXT {batchSize} ROWS ONLY
                     """;

        await using var cmd = new SqlCommand(query, connection);
        await using var reader = await cmd.ExecuteReaderAsync();

        var dataTable = new DataTable();
        dataTable.Load(reader);
        return dataTable;
    }
}
