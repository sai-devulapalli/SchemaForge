using SchemaForge.Services.Interfaces;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using SchemaForge.Models;

namespace SchemaForge.Services.SchemaReader;

/// <summary>
/// Reads schema metadata from SQL Server databases.
/// Extracts tables, columns, primary keys, foreign keys, indexes, constraints, and views.
/// </summary>
public class SqlServerSchemaReader(ILogger<SqlServerSchemaReader> logger) : ISchemaReader
{
    /// <summary>
    /// Reads all table schemas from SQL Server including columns, keys, indexes, and constraints.
    /// </summary>
    public async Task<List<TableSchema>> ReadSchemaAsync(string connectionString, IReadOnlyList<string>? includeTables = null, IReadOnlyList<string>? excludeTables = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString, nameof(connectionString));
        logger.LogInformation("Reading SQL Server schema...");
        var tables = new List<TableSchema>();

        await using var connection = new SqlConnection(connectionString);
        try
        {
            await connection.OpenAsync();
        }
        catch (SqlException ex)
        {
            logger.LogError(ex, "Failed to connect to SQL Server. Verify the connection string and server accessibility");
            throw new InvalidOperationException($"Failed to connect to SQL Server: {ex.Message}", ex);
        }

        var tableList = await GetTablesAsync(connection, includeTables, excludeTables);
        
        foreach (var (schema, tableName) in tableList)
        {
            logger.LogDebug("Reading schema for table: {Schema}.{Table}", schema, tableName);
            
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

        logger.LogInformation("Found {Count} tables", tables.Count);
        return tables;
    }

    /// <summary>
    /// Reads all view definitions from SQL Server.
    /// </summary>
    public async Task<List<ViewSchema>> ReadViewsAsync(string connectionString)
    {
        logger.LogInformation("Reading SQL Server views...");
        var views = new List<ViewSchema>();

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();

        var query = """
            SELECT 
                s.name AS schema_name,
                v.name AS view_name,
                m.definition
            FROM sys.views v
            INNER JOIN sys.schemas s ON v.schema_id = s.schema_id
            INNER JOIN sys.sql_modules m ON v.object_id = m.object_id
            WHERE s.name NOT IN ('sys', 'INFORMATION_SCHEMA')
            ORDER BY v.name
            """;

        await using var cmd = new SqlCommand(query, connection);
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

        logger.LogInformation("Found {Count} views", views.Count);
        return views;
    }

    private static async Task<List<(string Schema, string Name)>> GetTablesAsync(
        SqlConnection connection,
        IReadOnlyList<string>? includeTables = null,
        IReadOnlyList<string>? excludeTables = null)
    {
        var tables = new List<(string, string)>();

        var query = """
            SELECT TABLE_SCHEMA, TABLE_NAME
            FROM INFORMATION_SCHEMA.TABLES
            WHERE TABLE_TYPE = 'BASE TABLE'
            """;

        // Add include filter if specified
        if (includeTables is { Count: > 0 })
        {
            var includeList = string.Join(", ", includeTables.Select(t => $"'{t}'"));
            query += $" AND TABLE_NAME IN ({includeList})";
        }

        // Add exclude filter if specified
        if (excludeTables is { Count: > 0 })
        {
            var excludeList = string.Join(", ", excludeTables.Select(t => $"'{t}'"));
            query += $" AND TABLE_NAME NOT IN ({excludeList})";
        }

        query += " ORDER BY TABLE_NAME";

        await using var cmd = new SqlCommand(query, connection);
        await using var reader = await cmd.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            tables.Add((reader.GetString(0), reader.GetString(1)));
        }

        return tables;
    }

    // EXISTING METHOD - Keep as is
    private static async Task<List<ColumnSchema>> GetColumnsAsync(
        SqlConnection connection, 
        string schema, 
        string tableName)
    {
        var columns = new List<ColumnSchema>();
        var query = """
            SELECT 
                c.COLUMN_NAME,
                c.DATA_TYPE,
                c.CHARACTER_MAXIMUM_LENGTH,
                c.NUMERIC_PRECISION,
                c.NUMERIC_SCALE,
                c.IS_NULLABLE,
                c.COLUMN_DEFAULT,
                COLUMNPROPERTY(OBJECT_ID(c.TABLE_SCHEMA + '.' + c.TABLE_NAME), c.COLUMN_NAME, 'IsIdentity') as IS_IDENTITY
            FROM INFORMATION_SCHEMA.COLUMNS c
            WHERE c.TABLE_SCHEMA = @Schema AND c.TABLE_NAME = @TableName
            ORDER BY c.ORDINAL_POSITION
            """;

        await using var cmd = new SqlCommand(query, connection);
        cmd.Parameters.AddWithValue("@Schema", schema);
        cmd.Parameters.AddWithValue("@TableName", tableName);

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            columns.Add(new ColumnSchema
            {
                ColumnName = reader.GetString(0),
                DataType = reader.GetString(1),
                MaxLength = reader.IsDBNull(2) ? null : reader.GetInt32(2),
                Precision = reader.IsDBNull(3) ? null : reader.GetByte(3),
                Scale = reader.IsDBNull(4) ? null : reader.GetInt32(4),
                IsNullable = reader.GetString(5) == "YES",
                DefaultValue = reader.IsDBNull(6) ? null : reader.GetString(6),
                IsIdentity = reader.GetInt32(7) == 1
            });
        }

        return columns;
    }

    // EXISTING METHOD - Keep as is
    private static async Task<List<string>> GetPrimaryKeysAsync(
        SqlConnection connection, 
        string schema, 
        string tableName)
    {
        var primaryKeys = new List<string>();
        var query = """
            SELECT COL_NAME(ic.object_id, ic.column_id) AS COLUMN_NAME
            FROM sys.indexes i
            INNER JOIN sys.index_columns ic ON i.object_id = ic.object_id AND i.index_id = ic.index_id
            INNER JOIN sys.tables t ON i.object_id = t.object_id
            INNER JOIN sys.schemas s ON t.schema_id = s.schema_id
            WHERE i.is_primary_key = 1
                AND s.name = @Schema
                AND t.name = @TableName
            ORDER BY ic.key_ordinal
            """;

        await using var cmd = new SqlCommand(query, connection);
        cmd.Parameters.AddWithValue("@Schema", schema);
        cmd.Parameters.AddWithValue("@TableName", tableName);

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            primaryKeys.Add(reader.GetString(0));
        }

        return primaryKeys;
    }

    // EXISTING METHOD - Keep as is
    private static async Task<List<ForeignKeySchema>> GetForeignKeysAsync(
        SqlConnection connection, 
        string schema, 
        string tableName)
    {
        var foreignKeys = new List<ForeignKeySchema>();
        var query = """
            SELECT 
                fk.name AS FK_NAME,
                COL_NAME(fkc.parent_object_id, fkc.parent_column_id) AS COLUMN_NAME,
                OBJECT_SCHEMA_NAME(fk.referenced_object_id) AS REFERENCED_SCHEMA,
                OBJECT_NAME(fk.referenced_object_id) AS REFERENCED_TABLE,
                COL_NAME(fkc.referenced_object_id, fkc.referenced_column_id) AS REFERENCED_COLUMN
            FROM sys.foreign_keys fk
            INNER JOIN sys.foreign_key_columns fkc ON fk.object_id = fkc.constraint_object_id
            WHERE OBJECT_SCHEMA_NAME(fk.parent_object_id) = @Schema
                AND OBJECT_NAME(fk.parent_object_id) = @TableName
            """;

        await using var cmd = new SqlCommand(query, connection);
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

    // NEW METHOD: Get Indexes
    private static async Task<List<IndexSchema>> GetIndexesAsync(
        SqlConnection connection,
        string schema,
        string tableName)
    {
        var indexes = new List<IndexSchema>();
        var indexInfoList = new List<(string IndexName, bool IsUnique, bool IsPrimaryKey, string TypeDesc, string? FilterDefinition)>();

        var query = """
            SELECT
                i.name AS index_name,
                i.is_unique,
                i.is_primary_key,
                i.type_desc,
                i.filter_definition
            FROM sys.indexes i
            INNER JOIN sys.tables t ON i.object_id = t.object_id
            INNER JOIN sys.schemas s ON t.schema_id = s.schema_id
            WHERE s.name = @Schema
                AND t.name = @TableName
                AND i.is_hypothetical = 0
                AND i.type > 0
                AND i.is_unique_constraint = 0
            """;

        await using var cmd = new SqlCommand(query, connection);
        cmd.Parameters.AddWithValue("@Schema", schema);
        cmd.Parameters.AddWithValue("@TableName", tableName);

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            indexInfoList.Add((
                reader.GetString(0),
                reader.GetBoolean(1),
                reader.GetBoolean(2),
                reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4)
            ));
        }
        await reader.CloseAsync();

        // Now fetch columns for each index after the reader is closed
        foreach (var indexInfo in indexInfoList)
        {
            var indexColumns = await GetIndexColumnsAsync(connection, schema, tableName, indexInfo.IndexName);

            indexes.Add(new IndexSchema
            {
                IndexName = indexInfo.IndexName,
                TableName = tableName,
                SchemaName = schema,
                IsUnique = indexInfo.IsUnique,
                IsPrimaryKey = indexInfo.IsPrimaryKey,
                IsClustered = indexInfo.TypeDesc == "CLUSTERED",
                FilterExpression = indexInfo.FilterDefinition,
                Columns = indexColumns.KeyColumns,
                IncludedColumns = indexColumns.IncludedColumns
            });
        }

        return indexes;
    }

    private static async Task<(List<string> KeyColumns, List<string> IncludedColumns)> GetIndexColumnsAsync(
        SqlConnection connection,
        string schema,
        string tableName,
        string indexName)
    {
        var keyColumns = new List<string>();
        var includedColumns = new List<string>();

        var query = """
            SELECT 
                c.name,
                ic.is_included_column,
                ic.key_ordinal
            FROM sys.indexes i
            INNER JOIN sys.index_columns ic ON i.object_id = ic.object_id AND i.index_id = ic.index_id
            INNER JOIN sys.columns c ON ic.object_id = c.object_id AND ic.column_id = c.column_id
            INNER JOIN sys.tables t ON i.object_id = t.object_id
            INNER JOIN sys.schemas s ON t.schema_id = s.schema_id
            WHERE s.name = @Schema 
                AND t.name = @TableName
                AND i.name = @IndexName
            ORDER BY ic.key_ordinal, ic.index_column_id
            """;

        await using var cmd = new SqlCommand(query, connection);
        cmd.Parameters.AddWithValue("@Schema", schema);
        cmd.Parameters.AddWithValue("@TableName", tableName);
        cmd.Parameters.AddWithValue("@IndexName", indexName);

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var columnName = reader.GetString(0);
            var isIncluded = reader.GetBoolean(1);

            if (isIncluded)
            {
                includedColumns.Add(columnName);
            }
            else
            {
                keyColumns.Add(columnName);
            }
        }

        return (keyColumns, includedColumns);
    }

    // NEW METHOD: Get Constraints
    private static async Task<List<ConstraintSchema>> GetConstraintsAsync(
        SqlConnection connection, 
        string schema, 
        string tableName)
    {
        var constraints = new List<ConstraintSchema>();
        
        // Get Check Constraints
        var checkQuery = """
            SELECT 
                cc.name AS constraint_name,
                cc.definition
            FROM sys.check_constraints cc
            INNER JOIN sys.tables t ON cc.parent_object_id = t.object_id
            INNER JOIN sys.schemas s ON t.schema_id = s.schema_id
            WHERE s.name = @Schema AND t.name = @TableName
            """;

        await using var checkCmd = new SqlCommand(checkQuery, connection);
        checkCmd.Parameters.AddWithValue("@Schema", schema);
        checkCmd.Parameters.AddWithValue("@TableName", tableName);

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

        // Get Unique Constraints
        var uniqueQuery = """
            SELECT 
                kc.name AS constraint_name,
                c.name AS column_name
            FROM sys.key_constraints kc
            INNER JOIN sys.index_columns ic ON kc.parent_object_id = ic.object_id AND kc.unique_index_id = ic.index_id
            INNER JOIN sys.columns c ON ic.object_id = c.object_id AND ic.column_id = c.column_id
            INNER JOIN sys.tables t ON kc.parent_object_id = t.object_id
            INNER JOIN sys.schemas s ON t.schema_id = s.schema_id
            WHERE s.name = @Schema 
                AND t.name = @TableName 
                AND kc.type = 'UQ'
            ORDER BY kc.name, ic.key_ordinal
            """;

        await using var uniqueCmd = new SqlCommand(uniqueQuery, connection);
        uniqueCmd.Parameters.AddWithValue("@Schema", schema);
        uniqueCmd.Parameters.AddWithValue("@TableName", tableName);

        await using var uniqueReader = await uniqueCmd.ExecuteReaderAsync();
        
        var uniqueConstraints = new Dictionary<string, List<string>>();
        while (await uniqueReader.ReadAsync())
        {
            var constraintName = uniqueReader.GetString(0);
            var columnName = uniqueReader.GetString(1);

            if (!uniqueConstraints.ContainsKey(constraintName))
            {
                uniqueConstraints[constraintName] = new List<string>();
            }
            uniqueConstraints[constraintName].Add(columnName);
        }

        foreach (var (constraintName, columns) in uniqueConstraints)
        {
            constraints.Add(new ConstraintSchema
            {
                ConstraintName = constraintName,
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
                dc.name AS constraint_name,
                c.name AS column_name,
                dc.definition,
                tp.name AS data_type
            FROM sys.default_constraints dc
            INNER JOIN sys.columns c ON dc.parent_object_id = c.object_id AND dc.parent_column_id = c.column_id
            INNER JOIN sys.tables t ON dc.parent_object_id = t.object_id
            INNER JOIN sys.schemas s ON t.schema_id = s.schema_id
            INNER JOIN sys.types tp ON c.user_type_id = tp.user_type_id
            WHERE s.name = @Schema AND t.name = @TableName
            """;

        await using var defaultCmd = new SqlCommand(defaultQuery, connection);
        defaultCmd.Parameters.AddWithValue("@Schema", schema);
        defaultCmd.Parameters.AddWithValue("@TableName", tableName);

        await using var defaultReader = await defaultCmd.ExecuteReaderAsync();
        while (await defaultReader.ReadAsync())
        {
            constraints.Add(new ConstraintSchema
            {
                ConstraintName = defaultReader.GetString(0),
                TableName = tableName,
                SchemaName = schema,
                Type = ConstraintType.Default,
                Columns = new List<string> { defaultReader.GetString(1) },
                DefaultExpression = defaultReader.GetString(2),
                ColumnDataType = defaultReader.GetString(3)
            });
        }

        return constraints;
    }
}