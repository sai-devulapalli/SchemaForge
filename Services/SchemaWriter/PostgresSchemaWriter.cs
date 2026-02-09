using SchemaForge.Services.Interfaces;
using Microsoft.Extensions.Logging;
using Npgsql;
using SchemaForge.Models;
using System.Text;

namespace SchemaForge.Services.SchemaWriter;

/// <summary>
/// Creates schema objects in PostgreSQL databases.
/// Generates PostgreSQL-specific DDL for tables, views, indexes, and constraints.
/// </summary>
public class PostgresSchemaWriter(ILogger<PostgresSchemaWriter> logger,
                                    INamingConverter namingConverter,
                                    IDataTypeMapper dataTypeMapper,
                                    ISqlDialectConverter dialectConverter,
                                    ISqlCollector sqlCollector) : ISchemaWriter
{
    /// <summary>
    /// Creates schema, tables, and foreign keys in PostgreSQL.
    /// </summary>
    public async Task CreateSchemaAsync(
        string connectionString,
        string schemaName,
        List<TableSchema> tables)
    {
        logger.LogInformation("Creating PostgreSQL schema...");

        if (sqlCollector.IsCollecting)
        {
            sqlCollector.AddComment($"PostgreSQL Schema: {schemaName}");
            await CreateSchemaIfNotExistsAsync(null, schemaName);
            await CreateTablesAsync(null, schemaName, tables);
            await CreateForeignKeysAsync(null, schemaName, tables);
        }
        else
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();

            await CreateSchemaIfNotExistsAsync(connection, schemaName);
            await CreateTablesAsync(connection, schemaName, tables);
            await CreateForeignKeysAsync(connection, schemaName, tables);
        }

        logger.LogInformation("Schema creation completed");
    }

    /// <summary>
    /// Creates views in PostgreSQL with converted SQL definitions.
    /// </summary>
    public async Task CreateViewsAsync(string connectionString, string schemaName, List<ViewSchema> views)
    {
        logger.LogInformation("Creating PostgreSQL views...");

        NpgsqlConnection? connection = null;
        if (!sqlCollector.IsCollecting)
        {
            connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
        }

        try
        {
            foreach (var view in views)
            {
                var viewName = namingConverter.Convert(view.ViewName);
                var fullViewName = $"{schemaName}.\"{viewName}\"";

                var sourceDb = dialectConverter.DetectSourceDatabase(view.Definition);
                var convertedDefinition = dialectConverter.ConvertViewDefinition(
                    view.Definition,
                    sourceDb,
                    DatabaseTypes.PostgreSql
                );
                var sql = $"CREATE OR REPLACE VIEW {fullViewName} AS {convertedDefinition}";

                logger.LogInformation("Creating view: {Schema}.{View}", schemaName, viewName);

                if (sqlCollector.IsCollecting)
                {
                    sqlCollector.AddSql(sql, "Views", viewName);
                }
                else
                {
                    try
                    {
                        await using var cmd = new NpgsqlCommand(sql, connection);
                        await cmd.ExecuteNonQueryAsync();
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "Failed to create view {ViewName}. May need manual conversion.", viewName);
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
/// Creates indexes in PostgreSQL, including unique indexes and partial indexes.
/// </summary>
public async Task CreateIndexesAsync(string connectionString, string schemaName, List<IndexSchema> indexes)
{
    logger.LogInformation("Creating PostgreSQL indexes...");

    NpgsqlConnection? connection = null;
    if (!sqlCollector.IsCollecting)
    {
        connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
    }

    try
    {
        foreach (var index in indexes)
        {
            if (index.IsPrimaryKey) continue;

            var indexName = namingConverter.Convert(index.IndexName);
            var tableName = namingConverter.Convert(index.TableName);
            var fullTableName = $"{schemaName}.\"{tableName}\"";
            var columns = string.Join(", ", index.Columns.Select(c => $"\"{namingConverter.Convert(c)}\""));

            var unique = index.IsUnique ? "UNIQUE " : "";
            var sql = $"CREATE {unique}INDEX \"{indexName}\" ON {fullTableName} ({columns})";

            if (index.IncludedColumns.Any())
            {
                var included = string.Join(", ", index.IncludedColumns.Select(c => $"\"{namingConverter.Convert(c)}\""));
                sql += $" INCLUDE ({included})";
            }

            if (!string.IsNullOrEmpty(index.FilterExpression))
            {
                sql += $" WHERE {ConvertFilterExpression(index.FilterExpression)}";
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
                    await using var cmd = new NpgsqlCommand(sql, connection);
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
/// Creates constraints (CHECK, UNIQUE, DEFAULT) in PostgreSQL.
/// </summary>
public async Task CreateConstraintsAsync(string connectionString, string schemaName, List<ConstraintSchema> constraints)
{
    logger.LogInformation("Creating PostgreSQL constraints...");

    NpgsqlConnection? connection = null;
    if (!sqlCollector.IsCollecting)
    {
        connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
    }

    try
    {
        foreach (var constraint in constraints)
        {
            var constraintName = namingConverter.Convert(constraint.ConstraintName);
            var tableName = namingConverter.Convert(constraint.TableName);
            var fullTableName = $"{schemaName}.\"{tableName}\"";

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
                    await using var cmd = new NpgsqlCommand(sql, connection);
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
    var convertedExpression = ConvertCheckExpression(checkExpression);
    return $"ALTER TABLE {tableName} ADD CONSTRAINT \"{constraintName}\" CHECK ({convertedExpression})";
}

private string GenerateUniqueConstraint(string tableName, string constraintName, List<string> columns)
{
    var columnList = string.Join(", ", columns.Select(c => $"\"{namingConverter.Convert(c)}\""));
    return $"ALTER TABLE {tableName} ADD CONSTRAINT \"{constraintName}\" UNIQUE ({columnList})";
}

private string GenerateDefaultConstraint(string tableName, string constraintName, string columnName, string defaultExpression)
{
    var convertedColumn = namingConverter.Convert(columnName);
    var convertedExpression = ConvertDefaultExpression(defaultExpression);
    return $"ALTER TABLE {tableName} ALTER COLUMN \"{convertedColumn}\" SET DEFAULT {convertedExpression}";
}

private string ConvertFilterExpression(string expression)
{
    // Convert SQL Server syntax to PostgreSQL
    var converted = expression;
    converted = converted.Replace("[", "\"").Replace("]", "\"");
    return converted;
}

private string ConvertCheckExpression(string expression)
{
    // Remove CHECK keyword and parentheses if present
    var cleaned = expression.Trim();
    if (cleaned.StartsWith("CHECK", StringComparison.OrdinalIgnoreCase))
    {
        cleaned = cleaned.Substring(5).Trim();
    }
    cleaned = cleaned.Trim('(', ')');
    
    // Convert SQL Server functions to PostgreSQL
    cleaned = cleaned.Replace("[", "\"").Replace("]", "\"");
    
    return cleaned;
}

private string ConvertDefaultExpression(string expression)
{
    // Convert SQL Server defaults to PostgreSQL
    var cleaned = expression.Trim('(', ')');
    
    cleaned = cleaned.Replace("GETDATE()", "NOW()");
    cleaned = cleaned.Replace("NEWID()", "gen_random_uuid()");
    cleaned = cleaned.Replace("N'", "'");
    
    return cleaned;
}
    private async Task CreateSchemaIfNotExistsAsync(NpgsqlConnection? connection, string schemaName)
    {
        if (schemaName != "public")
        {
            ValidateIdentifier(schemaName);

            var sql = $"CREATE SCHEMA IF NOT EXISTS \"{schemaName}\"";

            if (sqlCollector.IsCollecting)
            {
                sqlCollector.AddSql(sql, "Schema", schemaName);
            }
            else
            {
                await using var cmd = new NpgsqlCommand(sql, connection);
                await cmd.ExecuteNonQueryAsync();
            }
            logger.LogInformation("Schema '{Schema}' created or already exists", schemaName);
        }
    }

    private static void ValidateIdentifier(string identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier))
            throw new ArgumentException("Identifier cannot be null or empty", nameof(identifier));

        // PostgreSQL identifiers: letters, digits, underscores
        // Must start with letter or underscore
        if (!System.Text.RegularExpressions.Regex.IsMatch(identifier, @"^[a-zA-Z_][a-zA-Z0-9_]*$"))
            throw new ArgumentException($"Invalid identifier: {identifier}", nameof(identifier));

        if (identifier.Length > 63)
            throw new ArgumentException($"Identifier too long: {identifier}", nameof(identifier));
    }

    private async Task CreateTablesAsync(
        NpgsqlConnection? connection,
        string schemaName,
        List<TableSchema> tables)
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
                await using var cmd = new NpgsqlCommand(sql, connection);
                await cmd.ExecuteNonQueryAsync();
            }
        }
    }

    private async Task CreateForeignKeysAsync(
        NpgsqlConnection? connection,
        string schemaName,
        List<TableSchema> tables)
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
                    var convertedTableName = namingConverter.Convert(table.TableName);
                    if (connection == null)
                    {
                        logger.LogWarning("Connection is null, skipping foreign key existence check for {FkName}", fk.Name);
                        continue;
                    }
                    if (await ForeignKeyExistsAsync(connection, schemaName, convertedTableName, fkName))
                    {
                        logger.LogWarning("Foreign key {FkName} on table {TableName} already exists. Skipping creation.", fk.Name, convertedTableName);
                        continue;
                    }

                    try
                    {
                        await using var cmd = new NpgsqlCommand(sql, connection);
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
        var fullTableName = $"{schemaName}.\"{tableName}\"";

        sb.AppendLine($"CREATE TABLE IF NOT EXISTS {fullTableName} (");

        var columnDefinitions = table.Columns
            .Select(col => $"    {GenerateColumnDefinition(col)}")
            .ToList();

        sb.AppendLine(string.Join(",\n", columnDefinitions));

        if (table.PrimaryKeys.Count > 0)
        {
            var pkColumns = string.Join(", ", table.PrimaryKeys.Select(pk => $"\"{namingConverter.Convert(pk)}\""));
            sb.AppendLine($",   CONSTRAINT \"pk_{tableName}\" PRIMARY KEY ({pkColumns})");
        }

        sb.AppendLine(");");
        return sb.ToString();
    }

    private string GenerateColumnDefinition(ColumnSchema column)
    {
        var columnName = $"\"{namingConverter.Convert(column.ColumnName)}\"";
        var dataType = dataTypeMapper.MapDataType(column, DatabaseTypes.PostgreSql);
        var nullable = column.IsNullable ? "" : " NOT NULL";
        var identity = column.IsIdentity ? " GENERATED BY DEFAULT AS IDENTITY" : "";

        return $"{columnName} {dataType}{nullable}{identity}";
    }

    private string GenerateAddForeignKeySql(string schemaName, TableSchema table, ForeignKeySchema fk)
    {
        var tableName = namingConverter.Convert(table.TableName);
        var fullTableName = $"{schemaName}.\"{tableName}\"";
        var columnName = $"\"{namingConverter.Convert(fk.ColumnName)}\"";
        var refTable = namingConverter.Convert(fk.ReferencedTable);
        var fullRefTable = $"{schemaName}.\"{refTable}\"";
        var refColumn = $"\"{namingConverter.Convert(fk.ReferencedColumn)}\"";
        var fkName = $"\"{namingConverter.Convert(fk.Name)}\"";

        return $"""
            ALTER TABLE {fullTableName} 
                ADD CONSTRAINT {fkName} 
                FOREIGN KEY ({columnName}) 
                REFERENCES {fullRefTable}({refColumn});
            """;
    }

    private async Task<bool> ForeignKeyExistsAsync(NpgsqlConnection connection, string schemaName, string tableName, string fkName)
    {
        var sql = $"""
            SELECT EXISTS (
                SELECT 1
                FROM pg_constraint c
                JOIN pg_class t ON c.conrelid = t.oid
                JOIN pg_namespace n ON t.relnamespace = n.oid
                WHERE n.nspname = @schemaName
                  AND t.relname = @tableName
                  AND c.conname = @fkName
                  AND c.contype = 'f'
            );
            """;

        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("schemaName", schemaName);
        cmd.Parameters.AddWithValue("tableName", tableName);
        cmd.Parameters.AddWithValue("fkName", fkName);

        return Convert.ToBoolean(await cmd.ExecuteScalarAsync());
    }
}