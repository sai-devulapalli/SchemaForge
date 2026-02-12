using SchemaForge.Services.Interfaces;
using Microsoft.Extensions.Logging;
using MySql.Data.MySqlClient;
using SchemaForge.Models;

namespace SchemaForge.Services.SchemaReader;

/// <summary>
/// Reads schema metadata from MySQL databases.
/// Extracts tables, columns, primary keys, foreign keys, indexes, constraints, and views.
/// </summary>
public class MySqlSchemaReader(ILogger<MySqlSchemaReader> logger) : ISchemaReader
{
    private readonly ILogger<MySqlSchemaReader> _logger = logger;

    /// <summary>
    /// Reads all table schemas from MySQL including columns, keys, indexes, and constraints.
    /// </summary>
    public async Task<List<TableSchema>> ReadSchemaAsync(string connectionString, IReadOnlyList<string>? includeTables = null, IReadOnlyList<string>? excludeTables = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString, nameof(connectionString));
        _logger.LogInformation("Reading MySQL schema...");
        var tables = new List<TableSchema>();

        await using var connection = new MySqlConnection(connectionString);
        try
        {
            await connection.OpenAsync();
        }
        catch (MySqlException ex)
        {
            _logger.LogError(ex, "Failed to connect to MySQL. Verify the connection string and server accessibility");
            throw new InvalidOperationException($"Failed to connect to MySQL: {ex.Message}", ex);
        }

        var tableList = await GetTablesAsync(connection, includeTables, excludeTables);
        
        foreach (var (schema, tableName) in tableList)
        {
            _logger.LogDebug("Reading schema for table: {Schema}.{Table}", schema, tableName);
            
            var table = new TableSchema
            {
                SchemaName = schema,
                TableName = tableName,
                Columns = await GetColumnsAsync(connection, schema, tableName),
                PrimaryKeys = await GetPrimaryKeysAsync(connection, schema, tableName),
                ForeignKeys = await GetForeignKeysAsync(connection, schema, tableName),
                Indexes = await GetIndexesAsync(connection, schema, tableName),        // NEW
                Constraints = await GetConstraintsAsync(connection, schema, tableName)  // NEW
            };
            tables.Add(table);
        }

        _logger.LogInformation("Found {Count} tables", tables.Count);
        return tables;
    }

    /// <summary>
    /// Reads all view definitions from MySQL.
    /// </summary>
    public async Task<List<ViewSchema>> ReadViewsAsync(string connectionString)
    {
        _logger.LogInformation("Reading MySQL views...");
        var views = new List<ViewSchema>();

        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync();

        var query = """
            SELECT 
                TABLE_SCHEMA,
                TABLE_NAME,
                VIEW_DEFINITION
            FROM INFORMATION_SCHEMA.VIEWS
            WHERE TABLE_SCHEMA = DATABASE()
            ORDER BY TABLE_NAME
            """;

        await using var cmd = new MySqlCommand(query, connection);
        await using var reader = await cmd.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            views.Add(new ViewSchema
            {
                SchemaName = reader.GetString(0),
                ViewName = reader.GetString(1),
                Definition = reader.GetString(2)
            });
        }

        _logger.LogInformation("Found {Count} views", views.Count);
        return views;
    }

    private static async Task<List<(string Schema, string Name)>> GetTablesAsync(
        MySqlConnection connection,
        IReadOnlyList<string>? includeTables = null,
        IReadOnlyList<string>? excludeTables = null)
    {
        var tables = new List<(string, string)>();
        var query = """
            SELECT TABLE_SCHEMA, TABLE_NAME
            FROM INFORMATION_SCHEMA.TABLES
            WHERE TABLE_TYPE = 'BASE TABLE'
                AND TABLE_SCHEMA = DATABASE()
            """;

        if (includeTables is { Count: > 0 })
        {
            var includeList = string.Join(", ", includeTables.Select(t => $"'{t}'"));
            query += $" AND TABLE_NAME IN ({includeList})";
        }

        if (excludeTables is { Count: > 0 })
        {
            var excludeList = string.Join(", ", excludeTables.Select(t => $"'{t}'"));
            query += $" AND TABLE_NAME NOT IN ({excludeList})";
        }

        query += " ORDER BY TABLE_NAME";

        await using var cmd = new MySqlCommand(query, connection);
        await using var reader = await cmd.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            tables.Add((reader.GetString(0), reader.GetString(1)));
        }

        return tables;
    }

    private static async Task<List<ColumnSchema>> GetColumnsAsync(
        MySqlConnection connection, 
        string schema, 
        string tableName)
    {
        var columns = new List<ColumnSchema>();
        var query = """
            SELECT 
                COLUMN_NAME,
                DATA_TYPE,
                CHARACTER_MAXIMUM_LENGTH,
                NUMERIC_PRECISION,
                NUMERIC_SCALE,
                IS_NULLABLE,
                COLUMN_DEFAULT,
                EXTRA
            FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_SCHEMA = @Schema AND TABLE_NAME = @TableName
            ORDER BY ORDINAL_POSITION
            """;

        await using var cmd = new MySqlCommand(query, connection);
        cmd.Parameters.AddWithValue("@Schema", schema);
        cmd.Parameters.AddWithValue("@TableName", tableName);

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            columns.Add(new ColumnSchema
            {
                ColumnName = reader.GetString(0),
                DataType = reader.GetString(1),
                MaxLength = reader.IsDBNull(2) ? null : Convert.ToInt32(reader.GetValue(2)),
                Precision = reader.IsDBNull(3) ? null : Convert.ToByte(reader.GetValue(3)),
                Scale = reader.IsDBNull(4) ? null : Convert.ToInt32(reader.GetValue(4)),
                IsNullable = reader.GetString(5) == "YES",
                DefaultValue = reader.IsDBNull(6) ? null : reader.GetString(6),
                IsIdentity = !reader.IsDBNull(7) && reader.GetString(7).Contains("auto_increment")
            });
        }

        return columns;
    }

    private static async Task<List<string>> GetPrimaryKeysAsync(
        MySqlConnection connection, 
        string schema, 
        string tableName)
    {
        var primaryKeys = new List<string>();
        var query = """
            SELECT COLUMN_NAME
            FROM INFORMATION_SCHEMA.KEY_COLUMN_USAGE
            WHERE TABLE_SCHEMA = @Schema 
                AND TABLE_NAME = @TableName 
                AND CONSTRAINT_NAME = 'PRIMARY'
            ORDER BY ORDINAL_POSITION
            """;

        await using var cmd = new MySqlCommand(query, connection);
        cmd.Parameters.AddWithValue("@Schema", schema);
        cmd.Parameters.AddWithValue("@TableName", tableName);

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            primaryKeys.Add(reader.GetString(0));
        }

        return primaryKeys;
    }

    private static async Task<List<ForeignKeySchema>> GetForeignKeysAsync(
        MySqlConnection connection, 
        string schema, 
        string tableName)
    {
        var foreignKeys = new List<ForeignKeySchema>();
        var query = """
            SELECT 
                CONSTRAINT_NAME,
                COLUMN_NAME,
                REFERENCED_TABLE_SCHEMA,
                REFERENCED_TABLE_NAME,
                REFERENCED_COLUMN_NAME
            FROM INFORMATION_SCHEMA.KEY_COLUMN_USAGE
            WHERE TABLE_SCHEMA = @Schema 
                AND TABLE_NAME = @TableName
                AND REFERENCED_TABLE_NAME IS NOT NULL
            """;

        await using var cmd = new MySqlCommand(query, connection);
        cmd.Parameters.AddWithValue("@Schema", schema);
        cmd.Parameters.AddWithValue("@TableName", tableName);

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            foreignKeys.Add(new ForeignKeySchema
            {
                Name = reader.GetString(0),
                ColumnName = reader.GetString(1),
                ReferencedSchema = reader.GetString(2),
                ReferencedTable = reader.GetString(3),
                ReferencedColumn = reader.GetString(4)
            });
        }

        return foreignKeys;
    }

    // NEW: Get Indexes
    private static async Task<List<IndexSchema>> GetIndexesAsync(
        MySqlConnection connection, 
        string schema, 
        string tableName)
    {
        var indexes = new List<IndexSchema>();
        
        var query = """
            SELECT 
                INDEX_NAME,
                NON_UNIQUE,
                GROUP_CONCAT(COLUMN_NAME ORDER BY SEQ_IN_INDEX) AS columns
            FROM INFORMATION_SCHEMA.STATISTICS
            WHERE TABLE_SCHEMA = @Schema 
                AND TABLE_NAME = @TableName
            GROUP BY INDEX_NAME, NON_UNIQUE
            """;

        await using var cmd = new MySqlCommand(query, connection);
        cmd.Parameters.AddWithValue("@Schema", schema);
        cmd.Parameters.AddWithValue("@TableName", tableName);

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var indexName = reader.GetString(0);
            var isPrimary = indexName == "PRIMARY";
            var isUnique = reader.GetInt32(1) == 0;
            var columnsStr = reader.GetString(2);
            var columns = columnsStr.Split(',').ToList();

            indexes.Add(new IndexSchema
            {
                IndexName = indexName,
                TableName = tableName,
                SchemaName = schema,
                IsUnique = isUnique,
                IsPrimaryKey = isPrimary,
                IsClustered = false, // MySQL doesn't have clustered indexes
                Columns = columns
            });
        }

        return indexes;
    }

    private async Task<List<ConstraintSchema>> GetConstraintsAsync(
        MySqlConnection connection, 
        string schema, 
        string tableName)
    {
        var constraints = new List<ConstraintSchema>();
        
        // Get Check Constraints (MySQL 8.0.16+)
        var checkQuery = """
            SELECT 
                CONSTRAINT_NAME,
                CHECK_CLAUSE
            FROM INFORMATION_SCHEMA.CHECK_CONSTRAINTS
            WHERE CONSTRAINT_SCHEMA = @Schema 
                AND TABLE_NAME = @TableName
            """;

        await using var checkCmd = new MySqlCommand(checkQuery, connection);
        checkCmd.Parameters.AddWithValue("@Schema", schema);
        checkCmd.Parameters.AddWithValue("@TableName", tableName);

        try
        {
            await using var checkReader = await checkCmd.ExecuteReaderAsync();
            while (await checkReader.ReadAsync())
            {
                constraints.Add(new ConstraintSchema
                {
                    ConstraintName = checkReader.GetString(0),
                    TableName = tableName,
                    SchemaName = schema,
                    Type = ConstraintType.Check,
                    Columns = new List<string>(),
                    CheckExpression = checkReader.GetString(1)
                });
            }
            await checkReader.CloseAsync();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not read check constraints - older MySQL versions may not support this feature");
        }

        // Get Unique Constraints
        var uniqueQuery = """
            SELECT 
                CONSTRAINT_NAME,
                GROUP_CONCAT(COLUMN_NAME ORDER BY ORDINAL_POSITION) AS columns
            FROM INFORMATION_SCHEMA.KEY_COLUMN_USAGE
            WHERE TABLE_SCHEMA = @Schema 
                AND TABLE_NAME = @TableName
                AND CONSTRAINT_NAME != 'PRIMARY'
                AND CONSTRAINT_NAME IN (
                    SELECT CONSTRAINT_NAME 
                    FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS 
                    WHERE CONSTRAINT_TYPE = 'UNIQUE'
                        AND TABLE_SCHEMA = @Schema
                        AND TABLE_NAME = @TableName
                )
            GROUP BY CONSTRAINT_NAME
            """;

        await using var uniqueCmd = new MySqlCommand(uniqueQuery, connection);
        uniqueCmd.Parameters.AddWithValue("@Schema", schema);
        uniqueCmd.Parameters.AddWithValue("@TableName", tableName);

        await using var uniqueReader = await uniqueCmd.ExecuteReaderAsync();
        while (await uniqueReader.ReadAsync())
        {
            var columnsStr = uniqueReader.GetString(1);
            var columns = columnsStr.Split(',').ToList();

            constraints.Add(new ConstraintSchema
            {
                ConstraintName = uniqueReader.GetString(0),
                TableName = tableName,
                SchemaName = schema,
                Type = ConstraintType.Unique,
                Columns = columns
            });
        }
        await uniqueReader.CloseAsync();

        // Get Default Constraints
        var defaultQuery = """
            SELECT
                COLUMN_NAME,
                COLUMN_DEFAULT,
                DATA_TYPE
            FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_SCHEMA = @Schema
                AND TABLE_NAME = @TableName
                AND COLUMN_DEFAULT IS NOT NULL
            """;

        await using var defaultCmd = new MySqlCommand(defaultQuery, connection);
        defaultCmd.Parameters.AddWithValue("@Schema", schema);
        defaultCmd.Parameters.AddWithValue("@TableName", tableName);

        await using var defaultReader = await defaultCmd.ExecuteReaderAsync();
        while (await defaultReader.ReadAsync())
        {
            constraints.Add(new ConstraintSchema
            {
                ConstraintName = $"df_{tableName}_{defaultReader.GetString(0)}",
                TableName = tableName,
                SchemaName = schema,
                Type = ConstraintType.Default,
                Columns = new List<string> { defaultReader.GetString(0) },
                DefaultExpression = defaultReader.GetString(1),
                ColumnDataType = defaultReader.GetString(2)
            });
        }

        return constraints;
    }
}