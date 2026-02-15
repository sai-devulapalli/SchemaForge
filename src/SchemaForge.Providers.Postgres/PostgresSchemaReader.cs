    using SchemaForge.Abstractions.Interfaces;
    using Microsoft.Extensions.Logging;
    using Npgsql;
    using SchemaForge.Abstractions.Models;
    
    namespace SchemaForge.Providers.Postgres;
    
    /// <summary>
    /// Reads schema metadata from PostgreSQL databases.
    /// Extracts tables, columns, primary keys, foreign keys, indexes, constraints, and views.
    /// </summary>
    public class PostgresSchemaReader(ILogger<PostgresSchemaReader> logger) : ISchemaReader
    {
        /// <summary>
        /// Reads all table schemas from PostgreSQL including columns, keys, indexes, and constraints.
        /// </summary>
        public async Task<List<TableSchema>> ReadSchemaAsync(string connectionString, IReadOnlyList<string>? includeTables = null, IReadOnlyList<string>? excludeTables = null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(connectionString, nameof(connectionString));
            logger.LogInformation("Reading PostgreSQL schema...");
            var tables = new List<TableSchema>();

            await using var connection = new NpgsqlConnection(connectionString);
            try
            {
                await connection.OpenAsync();
            }
            catch (NpgsqlException ex)
            {
                logger.LogError(ex, "Failed to connect to PostgreSQL. Verify the connection string and server accessibility");
                throw new InvalidOperationException($"Failed to connect to PostgreSQL: {ex.Message}", ex);
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
                    Indexes = await GetIndexesAsync(connection, schema, tableName),
                    Constraints = await GetConstraintsAsync(connection, schema, tableName)
                };
                tables.Add(table);
            }

            logger.LogInformation("Found {Count} tables", tables.Count);
            return tables;
        }
    /// <summary>
    /// Reads all view definitions from PostgreSQL.
    /// </summary>
    public async Task<List<ViewSchema>> ReadViewsAsync(string connectionString)
    {
        logger.LogInformation("Reading PostgreSQL views...");
        var views = new List<ViewSchema>();

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        var query = """
            SELECT 
                schemaname,
                viewname,
                definition
            FROM pg_views
            WHERE schemaname NOT IN ('pg_catalog', 'information_schema')
            ORDER BY viewname
            """;

        await using var cmd = new NpgsqlCommand(query, connection);
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

    private async Task<List<IndexSchema>> GetIndexesAsync(NpgsqlConnection connection, string schema, string tableName)
    {
        var indexes = new List<IndexSchema>();
        
        var query = """
            SELECT 
                i.indexname,
                i.indexdef,
                ix.indisunique,
                ix.indisprimary
            FROM pg_indexes i
            JOIN pg_class c ON c.relname = i.tablename
            JOIN pg_namespace n ON n.oid = c.relnamespace AND n.nspname = i.schemaname
            JOIN pg_index ix ON ix.indexrelid = (
                SELECT oid FROM pg_class WHERE relname = i.indexname AND relnamespace = n.oid
            )
            WHERE i.schemaname = @Schema 
                AND i.tablename = @TableName
            """;

        await using var cmd = new NpgsqlCommand(query, connection);
        cmd.Parameters.AddWithValue("@Schema", schema);
        cmd.Parameters.AddWithValue("@TableName", tableName);

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var indexDef = reader.GetString(1);
            var columns = ExtractColumnsFromIndexDef(indexDef);
            
            indexes.Add(new IndexSchema
            {
                IndexName = reader.GetString(0),
                TableName = tableName,
                SchemaName = schema,
                IsUnique = reader.GetBoolean(2),
                IsPrimaryKey = reader.GetBoolean(3),
                IsClustered = false, // PostgreSQL doesn't have clustered indexes
                Columns = columns
            });
        }

        return indexes;
    }

    private List<string> ExtractColumnsFromIndexDef(string indexDef)
    {
        // Parse: CREATE INDEX idx_name ON table (col1, col2)
        var start = indexDef.IndexOf('(');
        var end = indexDef.IndexOf(')');
        
        if (start == -1 || end == -1) return new List<string>();
        
        var columnsStr = indexDef.Substring(start + 1, end - start - 1);
        return columnsStr.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(c => c.Trim())
            .ToList();
    }

    private async Task<List<ConstraintSchema>> GetConstraintsAsync(NpgsqlConnection connection, string schema, string tableName)
    {
        var constraints = new List<ConstraintSchema>();
        
        constraints.AddRange(await GetCheckAndUniqueConstraintsAsync(connection, schema, tableName));
        constraints.AddRange(await GetDefaultConstraintsAsync(connection, schema, tableName));

        return constraints;
    }

    private async Task<List<ConstraintSchema>> GetCheckAndUniqueConstraintsAsync(NpgsqlConnection connection, string schema, string tableName)
    {
        var constraints = new List<ConstraintSchema>();
        
        var query = """
            SELECT 
                c.conname,
                c.contype,
                pg_get_constraintdef(c.oid) AS definition,
                ARRAY(
                    SELECT a.attname 
                    FROM unnest(c.conkey) WITH ORDINALITY AS u(attnum, ord)
                    JOIN pg_attribute a ON a.attnum = u.attnum AND a.attrelid = c.conrelid
                    ORDER BY u.ord
                ) AS columns
            FROM pg_constraint c
            JOIN pg_namespace n ON n.oid = c.connamespace
            JOIN pg_class t ON t.oid = c.conrelid
            WHERE n.nspname = @Schema 
                AND t.relname = @TableName
                AND c.contype IN ('c', 'u')  -- CHECK and UNIQUE only
            """;

        await using var cmd = new NpgsqlCommand(query, connection);
        cmd.Parameters.AddWithValue("@Schema", schema);
        cmd.Parameters.AddWithValue("@TableName", tableName);

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var conType = reader.GetString(1);
            var constraintType = conType switch
            {
                "c" => ConstraintType.Check,
                "u" => ConstraintType.Unique,
                _ => ConstraintType.Check
            };

            var definition = reader.GetString(2);
            var columns = reader.GetValue(3) as string[] ?? Array.Empty<string>();

            constraints.Add(new ConstraintSchema
            {
                ConstraintName = reader.GetString(0),
                TableName = tableName,
                SchemaName = schema,
                Type = constraintType,
                Columns = columns.ToList(),
                CheckExpression = constraintType == ConstraintType.Check ? definition : null
            });
        }

        return constraints;
    }

    private async Task<List<ConstraintSchema>> GetDefaultConstraintsAsync(NpgsqlConnection connection, string schema, string tableName)
    {
        var constraints = new List<ConstraintSchema>();

        // Get DEFAULT constraints separately
        var defaultQuery = """
            SELECT
                'df_' || a.attname AS constraint_name,
                a.attname AS column_name,
                pg_get_expr(d.adbin, d.adrelid) AS default_value,
                format_type(a.atttypid, a.atttypmod) AS data_type
            FROM pg_attrdef d
            JOIN pg_attribute a ON a.attrelid = d.adrelid AND a.attnum = d.adnum
            JOIN pg_class t ON t.oid = a.attrelid
            JOIN pg_namespace n ON n.oid = t.relnamespace
            WHERE n.nspname = @Schema
                AND t.relname = @TableName
            """;

        await using var defaultCmd = new NpgsqlCommand(defaultQuery, connection);
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
        private static async Task<List<(string Schema, string Name)>> GetTablesAsync(
            NpgsqlConnection connection,
            IReadOnlyList<string>? includeTables = null,
            IReadOnlyList<string>? excludeTables = null)
        {
            var tables = new List<(string, string)>();
            var query = """
                SELECT table_schema, table_name
                FROM information_schema.tables
                WHERE table_type = 'BASE TABLE'
                    AND table_schema NOT IN ('pg_catalog', 'information_schema')
                """;

            if (includeTables is { Count: > 0 })
            {
                var includeList = string.Join(", ", includeTables.Select(t => $"'{t}'"));
                query += $" AND table_name IN ({includeList})";
            }

            if (excludeTables is { Count: > 0 })
            {
                var excludeList = string.Join(", ", excludeTables.Select(t => $"'{t}'"));
                query += $" AND table_name NOT IN ({excludeList})";
            }

            query += " ORDER BY table_name";

            await using var cmd = new NpgsqlCommand(query, connection);
            await using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                tables.Add((reader.GetString(0), reader.GetString(1)));
            }

            return tables;
        }

        private static async Task<List<ColumnSchema>> GetColumnsAsync(
            NpgsqlConnection connection, 
            string schema, 
            string tableName)
        {
            var columns = new List<ColumnSchema>();
            var query = """
                SELECT 
                    c.column_name,
                    c.data_type,
                    c.character_maximum_length,
                    c.numeric_precision,
                    c.numeric_scale,
                    c.is_nullable,
                    c.column_default,
                    CASE 
                        WHEN c.column_default LIKE 'nextval%' THEN 1
                        ELSE 0
                    END as is_identity
                FROM information_schema.columns c
                WHERE c.table_schema = @Schema 
                    AND c.table_name = @TableName
                ORDER BY c.ordinal_position
                """;

            await using var cmd = new NpgsqlCommand(query, connection);
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
                    Precision = reader.IsDBNull(3) ? null : Convert.ToByte(reader.GetInt32(3)),
                    Scale = reader.IsDBNull(4) ? null : reader.GetInt32(4),
                    IsNullable = reader.GetString(5) == "YES",
                    DefaultValue = reader.IsDBNull(6) ? null : reader.GetString(6),
                    IsIdentity = reader.GetInt32(7) == 1
                });
            }

            return columns;
        }

        private static async Task<List<string>> GetPrimaryKeysAsync(
            NpgsqlConnection connection, 
            string schema, 
            string tableName)
        {
            var primaryKeys = new List<string>();
            var query = """
                SELECT a.attname
                FROM pg_index i
                JOIN pg_attribute a ON a.attrelid = i.indrelid AND a.attnum = ANY(i.indkey)
                JOIN pg_class t ON t.oid = i.indrelid
                JOIN pg_namespace n ON n.oid = t.relnamespace
                WHERE i.indisprimary
                    AND n.nspname = @Schema
                    AND t.relname = @TableName
                ORDER BY a.attnum
                """;

            await using var cmd = new NpgsqlCommand(query, connection);
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
            NpgsqlConnection connection, 
            string schema, 
            string tableName)
        {
            var foreignKeys = new List<ForeignKeySchema>();
            var query = """
                SELECT 
                    c.conname as constraint_name,
                    a.attname as column_name,
                    fn.nspname as referenced_schema,
                    ft.relname as referenced_table,
                    fa.attname as referenced_column
                FROM pg_constraint c
                JOIN pg_namespace n ON n.oid = c.connamespace
                JOIN pg_class t ON t.oid = c.conrelid
                JOIN pg_attribute a ON a.attrelid = c.conrelid AND a.attnum = ANY(c.conkey)
                JOIN pg_class ft ON ft.oid = c.confrelid
                JOIN pg_namespace fn ON fn.oid = ft.relnamespace
                JOIN pg_attribute fa ON fa.attrelid = c.confrelid AND fa.attnum = ANY(c.confkey)
                WHERE c.contype = 'f'
                    AND n.nspname = @Schema
                    AND t.relname = @TableName
                """;

            await using var cmd = new NpgsqlCommand(query, connection);
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
    }