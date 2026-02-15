using SchemaForge.Abstractions.Interfaces;
using Microsoft.Extensions.Logging;
using Oracle.ManagedDataAccess.Client;
using SchemaForge.Abstractions.Models;

namespace SchemaForge.Providers.Oracle;

/// <summary>
/// Reads schema metadata from Oracle databases.
/// Extracts tables, columns, primary keys, foreign keys, indexes, constraints, and views.
/// </summary>
public class OracleSchemaReader(ILogger<OracleSchemaReader> logger) : ISchemaReader
{
    /// <summary>
    /// Reads all table schemas from Oracle including columns, keys, indexes, and constraints.
    /// </summary>
    public async Task<List<TableSchema>> ReadSchemaAsync(string connectionString, IReadOnlyList<string>? includeTables = null, IReadOnlyList<string>? excludeTables = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString, nameof(connectionString));
        logger.LogInformation("Reading Oracle schema...");
        var tables = new List<TableSchema>();

        await using var connection = new OracleConnection(connectionString);
        try
        {
            await connection.OpenAsync();
        }
        catch (OracleException ex)
        {
            logger.LogError(ex, "Failed to connect to Oracle. Verify the connection string and server accessibility");
            throw new InvalidOperationException($"Failed to connect to Oracle: {ex.Message}", ex);
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
    /// Reads all view definitions from Oracle.
    /// </summary>
    public async Task<List<ViewSchema>> ReadViewsAsync(string connectionString)
    {
        logger.LogInformation("Reading Oracle views...");
        var views = new List<ViewSchema>();

        await using var connection = new OracleConnection(connectionString);
        await connection.OpenAsync();

        var query = """
                    SELECT 
                        OWNER,
                        VIEW_NAME,
                        TEXT
                    FROM ALL_VIEWS
                    WHERE OWNER = USER
                    ORDER BY VIEW_NAME
                    """;

        await using var cmd = new OracleCommand(query, connection);
        cmd.InitialLONGFetchSize = -1; // TEXT column in ALL_VIEWS is LONG type
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
        OracleConnection connection,
        IReadOnlyList<string>? includeTables = null,
        IReadOnlyList<string>? excludeTables = null)
    {
        var tables = new List<(string, string)>();
        var query = """
            SELECT OWNER, TABLE_NAME
            FROM ALL_TABLES
            WHERE OWNER = USER
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

        await using var cmd = new OracleCommand(query, connection);
        await using var reader = await cmd.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            tables.Add((reader.GetString(0), reader.GetString(1)));
        }

        return tables;
    }

    private static async Task<List<ColumnSchema>> GetColumnsAsync(
        OracleConnection connection, 
        string schema, 
        string tableName)
    {
        var columns = new List<ColumnSchema>();
        var query = """
            SELECT 
                c.COLUMN_NAME,
                c.DATA_TYPE,
                c.DATA_LENGTH,
                c.DATA_PRECISION,
                c.DATA_SCALE,
                c.NULLABLE,
                c.DATA_DEFAULT,
                CASE WHEN i.COLUMN_NAME IS NOT NULL THEN 1 ELSE 0 END as IS_IDENTITY
            FROM ALL_TAB_COLUMNS c
            LEFT JOIN ALL_TAB_IDENTITY_COLS i 
                ON c.OWNER = i.OWNER 
                AND c.TABLE_NAME = i.TABLE_NAME 
                AND c.COLUMN_NAME = i.COLUMN_NAME
            WHERE c.OWNER = :Schema AND c.TABLE_NAME = :TableName
            ORDER BY c.COLUMN_ID
            """;

        await using var cmd = new OracleCommand(query, connection);
        cmd.InitialLONGFetchSize = -1; // DATA_DEFAULT is a LONG column
        cmd.Parameters.Add(":Schema", OracleDbType.Varchar2).Value = schema;
        cmd.Parameters.Add(":TableName", OracleDbType.Varchar2).Value = tableName;

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
                IsNullable = reader.GetString(5) == "Y",
                DefaultValue = reader.IsDBNull(6) ? null : reader.GetString(6),
                IsIdentity = reader.GetInt32(7) == 1
            });
        }

        return columns;
    }

    private static async Task<List<string>> GetPrimaryKeysAsync(
        OracleConnection connection, 
        string schema, 
        string tableName)
    {
        var primaryKeys = new List<string>();
        var query = """
            SELECT cols.COLUMN_NAME
            FROM ALL_CONSTRAINTS cons
            JOIN ALL_CONS_COLUMNS cols 
                ON cons.CONSTRAINT_NAME = cols.CONSTRAINT_NAME
                AND cons.OWNER = cols.OWNER
            WHERE cons.CONSTRAINT_TYPE = 'P'
                AND cons.OWNER = :Schema
                AND cons.TABLE_NAME = :TableName
            ORDER BY cols.POSITION
            """;

        await using var cmd = new OracleCommand(query, connection);
        cmd.Parameters.Add(":Schema", OracleDbType.Varchar2).Value = schema;
        cmd.Parameters.Add(":TableName", OracleDbType.Varchar2).Value = tableName;

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            primaryKeys.Add(reader.GetString(0));
        }

        return primaryKeys;
    }

    private static async Task<List<ForeignKeySchema>> GetForeignKeysAsync(
        OracleConnection connection, 
        string schema, 
        string tableName)
    {
        var foreignKeys = new List<ForeignKeySchema>();
        var query = """
            SELECT 
                cons.CONSTRAINT_NAME,
                cols.COLUMN_NAME,
                r_cons.OWNER as REF_SCHEMA,
                r_cons.TABLE_NAME as REF_TABLE,
                r_cols.COLUMN_NAME as REF_COLUMN
            FROM ALL_CONSTRAINTS cons
            JOIN ALL_CONS_COLUMNS cols 
                ON cons.CONSTRAINT_NAME = cols.CONSTRAINT_NAME
                AND cons.OWNER = cols.OWNER
            JOIN ALL_CONSTRAINTS r_cons 
                ON cons.R_CONSTRAINT_NAME = r_cons.CONSTRAINT_NAME
                AND cons.R_OWNER = r_cons.OWNER
            JOIN ALL_CONS_COLUMNS r_cols 
                ON r_cons.CONSTRAINT_NAME = r_cols.CONSTRAINT_NAME
                AND r_cons.OWNER = r_cols.OWNER
            WHERE cons.CONSTRAINT_TYPE = 'R'
                AND cons.OWNER = :Schema
                AND cons.TABLE_NAME = :TableName
            """;

        await using var cmd = new OracleCommand(query, connection);
        cmd.Parameters.Add(":Schema", OracleDbType.Varchar2).Value = schema;
        cmd.Parameters.Add(":TableName", OracleDbType.Varchar2).Value = tableName;

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
    private static async Task<List<IndexSchema>> GetIndexesAsync(
        OracleConnection connection, 
        string schema, 
        string tableName)
    {
        var indexes = new List<IndexSchema>();
        
        var query = """
            SELECT 
                i.INDEX_NAME,
                i.UNIQUENESS,
                i.INDEX_TYPE
            FROM ALL_INDEXES i
            WHERE i.OWNER = :Schema 
                AND i.TABLE_NAME = :TableName
                AND i.INDEX_TYPE != 'LOB'
            """;

        await using var cmd = new OracleCommand(query, connection);
        cmd.Parameters.Add(":Schema", OracleDbType.Varchar2).Value = schema;
        cmd.Parameters.Add(":TableName", OracleDbType.Varchar2).Value = tableName;

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var indexName = reader.GetString(0);
            var columns = await GetIndexColumnsAsync(connection, schema, indexName);
            var isPrimary = await IsPrimaryKeyIndexAsync(connection, schema, tableName, indexName);

            indexes.Add(new IndexSchema
            {
                IndexName = indexName,
                TableName = tableName,
                SchemaName = schema,
                IsUnique = reader.GetString(1) == "UNIQUE",
                IsPrimaryKey = isPrimary,
                IsClustered = false, // Oracle doesn't have clustered indexes
                Columns = columns
            });
        }

        return indexes;
    }

    private static async Task<List<string>> GetIndexColumnsAsync(
        OracleConnection connection,
        string schema,
        string indexName)
    {
        var columns = new List<string>();

        var query = """
            SELECT COLUMN_NAME
            FROM ALL_IND_COLUMNS
            WHERE INDEX_OWNER = :Schema
                AND INDEX_NAME = :IndexName
            ORDER BY COLUMN_POSITION
            """;

        await using var cmd = new OracleCommand(query, connection);
        cmd.Parameters.Add(":Schema", OracleDbType.Varchar2).Value = schema;
        cmd.Parameters.Add(":IndexName", OracleDbType.Varchar2).Value = indexName;

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            columns.Add(reader.GetString(0));
        }

        return columns;
    }

    private static async Task<bool> IsPrimaryKeyIndexAsync(
        OracleConnection connection,
        string schema,
        string tableName,
        string indexName)
    {
        var query = """
            SELECT COUNT(*)
            FROM ALL_CONSTRAINTS
            WHERE OWNER = :Schema
                AND TABLE_NAME = :TableName
                AND CONSTRAINT_TYPE = 'P'
                AND INDEX_NAME = :IndexName
            """;

        await using var cmd = new OracleCommand(query, connection);
        cmd.Parameters.Add(":Schema", OracleDbType.Varchar2).Value = schema;
        cmd.Parameters.Add(":TableName", OracleDbType.Varchar2).Value = tableName;
        cmd.Parameters.Add(":IndexName", OracleDbType.Varchar2).Value = indexName;

        var result = await cmd.ExecuteScalarAsync();
        return Convert.ToInt32(result) > 0;
    }

    // NEW: Get Constraints
    private static async Task<List<ConstraintSchema>> GetConstraintsAsync(
        OracleConnection connection, 
        string schema, 
        string tableName)
    {
        var constraints = new List<ConstraintSchema>();
        
        // Get Check Constraints
        // Note: SEARCH_CONDITION is a LONG column in Oracle - can't use LIKE on it in SQL
        var checkQuery = """
            SELECT
                CONSTRAINT_NAME,
                SEARCH_CONDITION
            FROM ALL_CONSTRAINTS
            WHERE OWNER = :Schema
                AND TABLE_NAME = :TableName
                AND CONSTRAINT_TYPE = 'C'
            """;

        await using var checkCmd = new OracleCommand(checkQuery, connection);
        checkCmd.InitialLONGFetchSize = -1; // Enable LONG column fetching
        checkCmd.Parameters.Add(":Schema", OracleDbType.Varchar2).Value = schema;
        checkCmd.Parameters.Add(":TableName", OracleDbType.Varchar2).Value = tableName;

        await using var checkReader = await checkCmd.ExecuteReaderAsync();
        while (await checkReader.ReadAsync())
        {
            var searchCondition = checkReader.IsDBNull(1) ? "" : checkReader.GetString(1);
            // Filter out NOT NULL constraints in C# since SEARCH_CONDITION is LONG type
            if (searchCondition.Contains("IS NOT NULL", StringComparison.OrdinalIgnoreCase))
                continue;

            constraints.Add(new ConstraintSchema
            {
                ConstraintName = checkReader.GetString(0),
                TableName = tableName,
                SchemaName = schema,
                Type = ConstraintType.Check,
                Columns = new List<string>(),
                CheckExpression = searchCondition
            });
        }
        await checkReader.CloseAsync();

        // Get Unique Constraints
        var uniqueQuery = """
            SELECT 
                c.CONSTRAINT_NAME,
                LISTAGG(cc.COLUMN_NAME, ',') WITHIN GROUP (ORDER BY cc.POSITION) AS columns
            FROM ALL_CONSTRAINTS c
            JOIN ALL_CONS_COLUMNS cc ON c.CONSTRAINT_NAME = cc.CONSTRAINT_NAME AND c.OWNER = cc.OWNER
            WHERE c.OWNER = :Schema
                AND c.TABLE_NAME = :TableName
                AND c.CONSTRAINT_TYPE = 'U'
            GROUP BY c.CONSTRAINT_NAME
            """;

        await using var uniqueCmd = new OracleCommand(uniqueQuery, connection);
        uniqueCmd.Parameters.Add(":Schema", OracleDbType.Varchar2).Value = schema;
        uniqueCmd.Parameters.Add(":TableName", OracleDbType.Varchar2).Value = tableName;

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

        // Get Default Constraints (from column defaults)
        var defaultQuery = """
            SELECT
                COLUMN_NAME,
                DATA_DEFAULT,
                DATA_TYPE
            FROM ALL_TAB_COLUMNS
            WHERE OWNER = :Schema
                AND TABLE_NAME = :TableName
                AND DATA_DEFAULT IS NOT NULL
            """;

        await using var defaultCmd = new OracleCommand(defaultQuery, connection);
        defaultCmd.InitialLONGFetchSize = -1; // DATA_DEFAULT is a LONG column
        defaultCmd.Parameters.Add(":Schema", OracleDbType.Varchar2).Value = schema;
        defaultCmd.Parameters.Add(":TableName", OracleDbType.Varchar2).Value = tableName;

        await using var defaultReader = await defaultCmd.ExecuteReaderAsync();
        while (await defaultReader.ReadAsync())
        {
            var defaultValue = defaultReader.IsDBNull(1) ? null : defaultReader.GetString(1);
            if (string.IsNullOrWhiteSpace(defaultValue)) continue;

            constraints.Add(new ConstraintSchema
            {
                ConstraintName = $"DF_{tableName}_{defaultReader.GetString(0)}",
                TableName = tableName,
                SchemaName = schema,
                Type = ConstraintType.Default,
                Columns = new List<string> { defaultReader.GetString(0) },
                DefaultExpression = defaultValue,
                ColumnDataType = defaultReader.IsDBNull(2) ? null : defaultReader.GetString(2)
            });
        }

        return constraints;
    }
}