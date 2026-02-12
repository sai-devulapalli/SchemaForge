using SchemaForge.Services.Interfaces;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using SchemaForge.Models;
using System.Text;

namespace SchemaForge.Services.SchemaWriter;

/// <summary>
/// Creates schema objects in SQL Server databases.
/// Generates SQL Server-specific DDL for tables, views, indexes, and constraints.
/// </summary>
public class SqlServerSchemaWriter(
    ILogger<SqlServerSchemaWriter> logger,
    INamingConverter namingConverter,
    IDataTypeMapper dataTypeMapper,
    ISqlDialectConverter dialectConverter,
    ISqlCollector sqlCollector) : ISchemaWriter
{
    /// <summary>
    /// Creates schema, tables, and foreign keys in SQL Server.
    /// </summary>
    public async Task CreateSchemaAsync(string connectionString, string schemaName, List<TableSchema> tables)
    {
        logger.LogInformation("Creating SQL Server schema...");

        if (sqlCollector.IsCollecting)
        {
            sqlCollector.AddComment($"SQL Server Schema: {schemaName}");
            await CreateSchemaIfNotExistsAsync(null, schemaName);
            await CreateTablesAsync(null, schemaName, tables);
            await CreateForeignKeysAsync(null, schemaName, tables);
        }
        else
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();

            await CreateSchemaIfNotExistsAsync(connection, schemaName);
            await CreateTablesAsync(connection, schemaName, tables);
            await CreateForeignKeysAsync(connection, schemaName, tables);
        }

        logger.LogInformation("Schema creation completed");
    }

    /// <summary>
    /// Creates views in SQL Server with converted SQL definitions.
    /// </summary>
    public async Task CreateViewsAsync(string connectionString, string schemaName, List<ViewSchema> views,
        List<TableSchema>? sourceTables = null)
    {
        logger.LogInformation("Creating SQL Server views...");

        var tableNameMap = BuildTableNameMap(sourceTables);

        SqlConnection? connection = null;
        if (!sqlCollector.IsCollecting)
        {
            connection = new SqlConnection(connectionString);
            await connection.OpenAsync();
        }

        try
        {
            foreach (var view in views)
            {
                var viewName = namingConverter.Convert(view.ViewName);
                var fullViewName = $"[{schemaName}].[{viewName}]";

                var sourceDb = dialectConverter.DetectSourceDatabase(view.Definition);
                var convertedDefinition = dialectConverter.ConvertViewDefinition(
                    view.Definition,
                    sourceDb,
                    DatabaseTypes.SqlServer,
                    view.SchemaName,
                    schemaName,
                    tableNameMap
                );
                var sql = $"CREATE OR ALTER VIEW {fullViewName} AS {convertedDefinition}";

                logger.LogInformation("Creating view: {Schema}.{View}", schemaName, viewName);

                if (sqlCollector.IsCollecting)
                {
                    sqlCollector.AddSql(sql, "Views", viewName);
                }
                else
                {
                    try
                    {
                        await using var cmd = new SqlCommand(sql, connection);
                        await cmd.ExecuteNonQueryAsync();
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "Failed to create view {ViewName}", viewName);
                    }
                }
            }
        }
        finally
        {
            if (connection != null)
                await connection.DisposeAsync();
        }
    }

    /// <summary>
    /// Creates indexes in SQL Server, including clustered, filtered, and covering indexes.
    /// </summary>
    public async Task CreateIndexesAsync(string connectionString, string schemaName, List<IndexSchema> indexes)
    {
        logger.LogInformation("Creating SQL Server indexes...");

        SqlConnection? connection = null;
        if (!sqlCollector.IsCollecting)
        {
            connection = new SqlConnection(connectionString);
            await connection.OpenAsync();
        }

        try
        {
            foreach (var index in indexes)
            {
                if (index.IsPrimaryKey) continue;

                var indexName = namingConverter.Convert(index.IndexName);
                var tableName = namingConverter.Convert(index.TableName);
                var fullTableName = $"[{schemaName}].[{tableName}]";
                var columns = string.Join(", ", index.Columns.Select(c => $"[{namingConverter.Convert(c)}]"));

                var unique = index.IsUnique ? "UNIQUE " : "";
                var clustered = index.IsClustered ? "CLUSTERED " : "NONCLUSTERED ";
                var sql = $"CREATE {unique}{clustered}INDEX [{indexName}] ON {fullTableName} ({columns})";

                if (index.IncludedColumns.Count > 0)
                {
                    var included = string.Join(", ", index.IncludedColumns.Select(c => $"[{namingConverter.Convert(c)}]"));
                    sql += $" INCLUDE ({included})";
                }

                if (!string.IsNullOrEmpty(index.FilterExpression))
                {
                    sql += $" WHERE {index.FilterExpression}";
                }

                logger.LogInformation("Creating index: {IndexName} on {Table}", indexName, tableName);

                if (sqlCollector.IsCollecting)
                {
                    sqlCollector.AddSql(sql, "Indexes", indexName);
                }
                else
                {
                    try
                    {
                        await using var cmd = new SqlCommand(sql, connection);
                        await cmd.ExecuteNonQueryAsync();
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "Failed to create index {IndexName}", indexName);
                    }
                }
            }
        }
        finally
        {
            if (connection != null)
                await connection.DisposeAsync();
        }
    }

    /// <summary>
    /// Creates constraints (CHECK, UNIQUE, DEFAULT) in SQL Server.
    /// </summary>
    public async Task CreateConstraintsAsync(string connectionString, string schemaName, List<ConstraintSchema> constraints)
    {
        logger.LogInformation("Creating SQL Server constraints...");

        SqlConnection? connection = null;
        if (!sqlCollector.IsCollecting)
        {
            connection = new SqlConnection(connectionString);
            await connection.OpenAsync();
        }

        try
        {
            foreach (var constraint in constraints)
            {
                var constraintName = namingConverter.Convert(constraint.ConstraintName);
                var tableName = namingConverter.Convert(constraint.TableName);
                var fullTableName = $"[{schemaName}].[{tableName}]";

                string sql = constraint.Type switch
                {
                    ConstraintType.Check => GenerateCheckConstraint(fullTableName, constraintName, constraint.CheckExpression!),
                    ConstraintType.Unique => GenerateUniqueConstraint(fullTableName, constraintName, constraint.Columns),
                    ConstraintType.Default => GenerateDefaultConstraint(fullTableName, constraintName, constraint.Columns[0], constraint.DefaultExpression!),
                    _ => ""
                };

                if (string.IsNullOrEmpty(sql)) continue;

                logger.LogInformation("Creating constraint: {ConstraintName} on {Table}", constraintName, tableName);

                if (sqlCollector.IsCollecting)
                {
                    sqlCollector.AddSql(sql, "Constraints", constraintName);
                }
                else
                {
                    try
                    {
                        await using var cmd = new SqlCommand(sql, connection);
                        await cmd.ExecuteNonQueryAsync();
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "Failed to create constraint {ConstraintName}", constraintName);
                    }
                }
            }
        }
        finally
        {
            if (connection != null)
                await connection.DisposeAsync();
        }
    }

    private string GenerateCheckConstraint(string tableName, string constraintName, string checkExpression)
    {
        var converted = dialectConverter.ConvertCheckExpression(checkExpression, dialectConverter.DetectSourceDatabase(checkExpression), DatabaseTypes.SqlServer);
        return $"ALTER TABLE {tableName} ADD CONSTRAINT [{constraintName}] CHECK ({converted})";
    }

    private string GenerateUniqueConstraint(string tableName, string constraintName, List<string> columns)
    {
        var columnList = string.Join(", ", columns.Select(c => $"[{namingConverter.Convert(c)}]"));
        return $"ALTER TABLE {tableName} ADD CONSTRAINT [{constraintName}] UNIQUE ({columnList})";
    }

    private string GenerateDefaultConstraint(string tableName, string constraintName, string columnName, string defaultExpression)
    {
        var convertedColumn = namingConverter.Convert(columnName);
        var converted = dialectConverter.ConvertDefaultExpression(defaultExpression, dialectConverter.DetectSourceDatabase(defaultExpression), DatabaseTypes.SqlServer);
        return $"ALTER TABLE {tableName} ADD CONSTRAINT [{constraintName}] DEFAULT {converted} FOR [{convertedColumn}]";
    }

    private async Task CreateSchemaIfNotExistsAsync(SqlConnection? connection, string schemaName)
    {
        if (schemaName != "dbo")
        {
            ValidateIdentifier(schemaName);

            var createSql = $"CREATE SCHEMA [{schemaName}]";

            if (sqlCollector.IsCollecting)
            {
                // In dry run, just output the CREATE SCHEMA (existence check not possible)
                sqlCollector.AddSql(createSql, "Schema", schemaName);
            }
            else
            {
                var checkSql = "SELECT COUNT(*) FROM sys.schemas WHERE name = @SchemaName";
                await using var checkCmd = new SqlCommand(checkSql, connection);
                checkCmd.Parameters.AddWithValue("@SchemaName", schemaName);
                var exists = (int)(await checkCmd.ExecuteScalarAsync() ?? 0) > 0;

                if (!exists)
                {
                    await using var createCmd = new SqlCommand(createSql, connection);
                    await createCmd.ExecuteNonQueryAsync();
                }
            }

            logger.LogInformation("Schema '{Schema}' created or already exists", schemaName);
        }
    }

    private static void ValidateIdentifier(string identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier))
            throw new ArgumentException("Identifier cannot be null or empty", nameof(identifier));

        // SQL Server identifiers: letters, digits, underscores, @, #, $
        // Must start with letter, underscore, @, or #
        if (!System.Text.RegularExpressions.Regex.IsMatch(identifier, @"^[a-zA-Z_@#][a-zA-Z0-9_@#$]*$"))
            throw new ArgumentException($"Invalid identifier: {identifier}", nameof(identifier));

        if (identifier.Length > 128)
            throw new ArgumentException($"Identifier too long: {identifier}", nameof(identifier));
    }

    private async Task CreateTablesAsync(SqlConnection? connection, string schemaName, List<TableSchema> tables)
    {
        foreach (var table in tables)
        {
            var sql = GenerateCreateTableSql(schemaName, table);
            var convertedTableName = namingConverter.Convert(table.TableName);

            logger.LogInformation("Creating table: {Schema}.{Table}", schemaName, convertedTableName);

            if (sqlCollector.IsCollecting)
            {
                sqlCollector.AddSql(sql, "Tables", convertedTableName);
            }
            else
            {
                await using var cmd = new SqlCommand(sql, connection);
                await cmd.ExecuteNonQueryAsync();
            }
        }
    }

    private async Task CreateForeignKeysAsync(SqlConnection? connection, string schemaName, List<TableSchema> tables)
    {
        foreach (var table in tables.Where(t => t.ForeignKeys.Count > 0))
        {
            foreach (var fk in table.ForeignKeys)
            {
                var sql = GenerateAddForeignKeySql(schemaName, table, fk);
                var fkName = namingConverter.Convert(fk.Name);

                if (sqlCollector.IsCollecting)
                {
                    sqlCollector.AddSql(sql, "ForeignKeys", fkName);
                }
                else
                {
                    try
                    {
                        await using var cmd = new SqlCommand(sql, connection);
                        await cmd.ExecuteNonQueryAsync();
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "Failed to create foreign key {FkName}", fk.Name);
                    }
                }
            }
        }
    }

    private string GenerateCreateTableSql(string schemaName, TableSchema table)
    {
        var sb = new StringBuilder();
        var tableName = namingConverter.Convert(table.TableName);
        var fullTableName = $"[{schemaName}].[{tableName}]";

        sb.AppendLine($"CREATE TABLE {fullTableName} (");

        var columnDefinitions = table.Columns.Select(col => $"    {GenerateColumnDefinition(col)}").ToList();
        sb.AppendLine(string.Join(",\n", columnDefinitions));

        if (table.PrimaryKeys.Count > 0)
        {
            var pkColumns = string.Join(", ", table.PrimaryKeys.Select(pk => $"[{namingConverter.Convert(pk)}]"));
            sb.AppendLine($",   CONSTRAINT [PK_{tableName}] PRIMARY KEY CLUSTERED ({pkColumns})");
        }

        sb.AppendLine(");");
        return sb.ToString();
    }

    private string GenerateColumnDefinition(ColumnSchema column)
    {
        var columnName = $"[{namingConverter.Convert(column.ColumnName)}]";
        var dataType = dataTypeMapper.MapDataType(column, DatabaseTypes.SqlServer); // Pass target DB
        var nullable = column.IsNullable ? "NULL" : "NOT NULL";
        var identity = column.IsIdentity ? " IDENTITY(1,1)" : "";

        return $"{columnName} {dataType} {nullable}{identity}";
    }

    private string GenerateAddForeignKeySql(string schemaName, TableSchema table, ForeignKeySchema fk)
    {
        var tableName = namingConverter.Convert(table.TableName);
        var fullTableName = $"[{schemaName}].[{tableName}]";
        var columnName = $"[{namingConverter.Convert(fk.ColumnName)}]";
        var refTable = namingConverter.Convert(fk.ReferencedTable);
        var fullRefTable = $"[{schemaName}].[{refTable}]";
        var refColumn = $"[{namingConverter.Convert(fk.ReferencedColumn)}]";
        var fkName = $"[FK_{namingConverter.Convert(fk.Name)}]";

        return $"ALTER TABLE {fullTableName} ADD CONSTRAINT {fkName} FOREIGN KEY ({columnName}) REFERENCES {fullRefTable}({refColumn})";
    }

    private Dictionary<string, string>? BuildTableNameMap(List<TableSchema>? sourceTables)
    {
        if (sourceTables == null || sourceTables.Count == 0) return null;
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var table in sourceTables)
        {
            var convertedTable = namingConverter.Convert(table.TableName);
            if (table.TableName != convertedTable)
                map[table.TableName] = convertedTable;
            foreach (var col in table.Columns)
            {
                var convertedCol = namingConverter.Convert(col.ColumnName);
                if (col.ColumnName != convertedCol && !map.ContainsKey(col.ColumnName))
                    map[col.ColumnName] = convertedCol;
            }
        }
        return map.Count > 0 ? map : null;
    }
}