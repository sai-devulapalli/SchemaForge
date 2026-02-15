using SchemaForge.Abstractions.Interfaces;
using Microsoft.Extensions.Logging;
using MySql.Data.MySqlClient;
using SchemaForge.Abstractions.Models;
using System.Text;

namespace SchemaForge.Providers.MySql;

/// <summary>
/// Creates schema objects in MySQL databases.
/// Generates MySQL-specific DDL for tables, views, indexes, and constraints.
/// </summary>
public class MySqlSchemaWriter(
    ILogger<MySqlSchemaWriter> logger,
    INamingConverter namingConverter,
    IDataTypeMapper dataTypeMapper,
    ISqlDialectConverter dialectConverter,
    ISqlCollector sqlCollector) : ISchemaWriter
{
    /// <summary>
    /// Creates database, tables, and foreign keys in MySQL.
    /// </summary>
    public async Task CreateSchemaAsync(
        string connectionString,
        string schemaName,
        List<TableSchema> tables)
    {
        logger.LogInformation("Creating MySQL schema...");

        if (sqlCollector.IsCollecting)
        {
            sqlCollector.AddComment($"MySQL Database: {schemaName}");
            await CreateDatabaseIfNotExistsAsync(null, schemaName);
            await CreateTablesAsync(null, schemaName, tables);
            await CreateForeignKeysAsync(null, schemaName, tables);
        }
        else
        {
            await using var connection = new MySqlConnection(connectionString);
            await connection.OpenAsync();

            await CreateDatabaseIfNotExistsAsync(connection, schemaName);
            await CreateTablesAsync(connection, schemaName, tables);
            await CreateForeignKeysAsync(connection, schemaName, tables);
        }

        logger.LogInformation("Schema creation completed");
    }

    /// <summary>
    /// Creates views in MySQL with converted SQL definitions.
    /// </summary>
    public async Task CreateViewsAsync(string connectionString, string schemaName, List<ViewSchema> views,
        List<TableSchema>? sourceTables = null)
    {
        logger.LogInformation("Creating MySQL views...");

        var tableNameMap = BuildTableNameMap(sourceTables);

        MySqlConnection? connection = null;
        if (!sqlCollector.IsCollecting)
        {
            connection = new MySqlConnection(connectionString);
            await connection.OpenAsync();

            var useSql = $"USE `{schemaName}`";
            await using var useCmd = new MySqlCommand(useSql, connection);
            await useCmd.ExecuteNonQueryAsync();
        }

        try
        {
            foreach (var view in views)
            {
                var viewName = namingConverter.Convert(view.ViewName);

                var sourceDb = dialectConverter.DetectSourceDatabase(view.Definition);
                var convertedDefinition = dialectConverter.ConvertViewDefinition(
                    view.Definition,
                    sourceDb,
                    DatabaseTypes.MySql,
                    view.SchemaName,
                    schemaName,
                    tableNameMap
                );
                var sql = $"CREATE OR REPLACE VIEW `{viewName}` AS {convertedDefinition}";

                logger.LogInformation("Creating view: {Schema}.{View}", schemaName, viewName);

                if (sqlCollector.IsCollecting)
                {
                    sqlCollector.AddSql(sql, "Views", viewName);
                }
                else
                {
                    try
                    {
                        await using var cmd = new MySqlCommand(sql, connection);
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
    /// Creates indexes in MySQL.
    /// </summary>
    public async Task CreateIndexesAsync(string connectionString, string schemaName, List<IndexSchema> indexes)
    {
        logger.LogInformation("Creating MySQL indexes...");

        MySqlConnection? connection = null;
        if (!sqlCollector.IsCollecting)
        {
            connection = new MySqlConnection(connectionString);
            await connection.OpenAsync();

            var useSql = $"USE `{schemaName}`";
            await using var useCmd = new MySqlCommand(useSql, connection);
            await useCmd.ExecuteNonQueryAsync();
        }

        try
        {
            foreach (var index in indexes)
            {
                if (index.IsPrimaryKey) continue;

                var indexName = namingConverter.Convert(index.IndexName);
                var tableName = namingConverter.Convert(index.TableName);
                var columns = string.Join(", ", index.Columns.Select(c => $"`{namingConverter.Convert(c)}`"));

                var unique = index.IsUnique ? "UNIQUE " : "";
                var sql = $"CREATE {unique}INDEX `{indexName}` ON `{tableName}` ({columns})";

                logger.LogInformation("Creating index: {IndexName} on {Table}", indexName, tableName);

                if (sqlCollector.IsCollecting)
                {
                    sqlCollector.AddSql(sql, "Indexes", indexName);
                }
                else
                {
                    try
                    {
                        await using var cmd = new MySqlCommand(sql, connection);
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
    /// Creates constraints (CHECK, UNIQUE, DEFAULT) in MySQL.
    /// </summary>
    public async Task CreateConstraintsAsync(string connectionString, string schemaName, List<ConstraintSchema> constraints)
    {
        logger.LogInformation("Creating MySQL constraints...");

        MySqlConnection? connection = null;
        if (!sqlCollector.IsCollecting)
        {
            connection = new MySqlConnection(connectionString);
            await connection.OpenAsync();

            var useSql = $"USE `{schemaName}`";
            await using var useCmd = new MySqlCommand(useSql, connection);
            await useCmd.ExecuteNonQueryAsync();
        }

        try
        {
            foreach (var constraint in constraints)
            {
                var constraintName = namingConverter.Convert(constraint.ConstraintName);
                var tableName = namingConverter.Convert(constraint.TableName);

                string sql = constraint.Type switch
                {
                    ConstraintType.Check => GenerateCheckConstraint(tableName, constraintName, constraint.CheckExpression!),
                    ConstraintType.Unique => GenerateUniqueConstraint(tableName, constraintName, constraint.Columns),
                    ConstraintType.Default => GenerateDefaultConstraint(tableName, constraint.Columns[0], constraint.DefaultExpression!),
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
                        await using var cmd = new MySqlCommand(sql, connection);
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
        var converted = dialectConverter.ConvertCheckExpression(checkExpression, dialectConverter.DetectSourceDatabase(checkExpression), DatabaseTypes.MySql);
        return $"ALTER TABLE `{tableName}` ADD CONSTRAINT `{constraintName}` CHECK ({converted})";
    }

    private string GenerateUniqueConstraint(string tableName, string constraintName, List<string> columns)
    {
        var columnList = string.Join(", ", columns.Select(c => $"`{namingConverter.Convert(c)}`"));
        return $"ALTER TABLE `{tableName}` ADD CONSTRAINT `{constraintName}` UNIQUE ({columnList})";
    }

    private string GenerateDefaultConstraint(string tableName, string columnName, string defaultExpression)
    {
        var convertedColumn = namingConverter.Convert(columnName);
        var converted = dialectConverter.ConvertDefaultExpression(defaultExpression, dialectConverter.DetectSourceDatabase(defaultExpression), DatabaseTypes.MySql);
        if (string.IsNullOrWhiteSpace(converted)) return "";
        return $"ALTER TABLE `{tableName}` ALTER COLUMN `{convertedColumn}` SET DEFAULT {converted}";
    }

    private async Task CreateDatabaseIfNotExistsAsync(MySqlConnection? connection, string schemaName)
    {
        ValidateIdentifier(schemaName);

        var createSql = $"CREATE DATABASE IF NOT EXISTS `{schemaName}`";
        var useSql = $"USE `{schemaName}`";

        if (sqlCollector.IsCollecting)
        {
            sqlCollector.AddSql(createSql, "Schema", schemaName);
            sqlCollector.AddSql(useSql, "Schema", schemaName);
        }
        else
        {
            await using var createCmd = new MySqlCommand(createSql, connection);
            await createCmd.ExecuteNonQueryAsync();

            await using var useCmd = new MySqlCommand(useSql, connection);
            await useCmd.ExecuteNonQueryAsync();
        }

        logger.LogInformation("Database '{Schema}' created or already exists", schemaName);
    }

    private static void ValidateIdentifier(string identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier))
            throw new ArgumentException("Identifier cannot be null or empty", nameof(identifier));

        // MySQL identifiers: letters, digits, underscores, $
        // Cannot be purely numeric
        if (!System.Text.RegularExpressions.Regex.IsMatch(identifier, @"^[a-zA-Z_$][a-zA-Z0-9_$]*$"))
            throw new ArgumentException($"Invalid identifier: {identifier}", nameof(identifier));

        if (identifier.Length > 64)
            throw new ArgumentException($"Identifier too long: {identifier}", nameof(identifier));
    }

    private async Task CreateTablesAsync(
        MySqlConnection? connection,
        string schemaName,
        List<TableSchema> tables)
    {
        foreach (var table in tables)
        {
            var sql = GenerateCreateTableSql(table);
            var convertedTableName = namingConverter.Convert(table.TableName);

            logger.LogInformation("Creating table: {Schema}.{Table}", schemaName, convertedTableName);

            if (sqlCollector.IsCollecting)
            {
                sqlCollector.AddSql(sql, "Tables", convertedTableName);
            }
            else
            {
                await using var cmd = new MySqlCommand(sql, connection);
                await cmd.ExecuteNonQueryAsync();
            }
        }
    }

    private async Task CreateForeignKeysAsync(
        MySqlConnection? connection,
        string schemaName,
        List<TableSchema> tables)
    {
        foreach (var table in tables.Where(t => t.ForeignKeys.Count > 0))
        {
            foreach (var fk in table.ForeignKeys)
            {
                var sql = GenerateAddForeignKeySql(table, fk);
                var fkName = namingConverter.Convert(fk.Name);

                if (sqlCollector.IsCollecting)
                {
                    sqlCollector.AddSql(sql, "ForeignKeys", fkName);
                }
                else
                {
                    try
                    {
                        await using var cmd = new MySqlCommand(sql, connection);
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

    private string GenerateCreateTableSql(TableSchema table)
    {
        var sb = new StringBuilder();
        var tableName = namingConverter.Convert(table.TableName);

        sb.AppendLine($"CREATE TABLE `{tableName}` (");

        var columnDefinitions = table.Columns
            .Select(col => $"    {GenerateColumnDefinition(col)}")
            .ToList();

        sb.AppendLine(string.Join(",\n", columnDefinitions));

        if (table.PrimaryKeys.Count > 0)
        {
            var pkColumns = string.Join(", ", table.PrimaryKeys.Select(pk => $"`{namingConverter.Convert(pk)}`"));
            sb.AppendLine($",   PRIMARY KEY ({pkColumns})");
        }

        sb.AppendLine(") ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;");
        return sb.ToString();
    }

    private string GenerateColumnDefinition(ColumnSchema column)
    {
        var columnName = $"`{namingConverter.Convert(column.ColumnName)}`";
        var dataType = dataTypeMapper.MapDataType(column, DatabaseTypes.MySql); // Pass target DB
        var nullable = column.IsNullable ? "NULL" : "NOT NULL";
        var identity = column.IsIdentity ? " AUTO_INCREMENT" : "";

        return $"{columnName} {dataType} {nullable}{identity}";
    }

    private string GenerateAddForeignKeySql(TableSchema table, ForeignKeySchema fk)
    {
        var tableName = namingConverter.Convert(table.TableName);
        var columnName = namingConverter.Convert(fk.ColumnName);
        var refTable = namingConverter.Convert(fk.ReferencedTable);
        var refColumn = namingConverter.Convert(fk.ReferencedColumn);
        var fkName = namingConverter.Convert(fk.Name);

        return $"""
            ALTER TABLE `{tableName}`
                ADD CONSTRAINT `{fkName}`
                FOREIGN KEY (`{columnName}`)
                REFERENCES `{refTable}`(`{refColumn}`);
            """;
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