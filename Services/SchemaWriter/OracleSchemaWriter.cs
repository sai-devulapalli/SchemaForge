using SchemaForge.Services.Interfaces;
using Microsoft.Extensions.Logging;
using Oracle.ManagedDataAccess.Client;
using SchemaForge.Models;
using System.Text;
using System.Text.RegularExpressions;

namespace SchemaForge.Services.SchemaWriter;

/// <summary>
/// Creates schema objects in Oracle databases.
/// Generates Oracle-specific DDL for tables, views, indexes, and constraints.
/// </summary>
public class OracleSchemaWriter(ILogger<OracleSchemaWriter> logger, INamingConverter namingConverter,
                                IDataTypeMapper dataTypeMapper,
                                ISqlDialectConverter dialectConverter,
                                ISqlCollector sqlCollector) : ISchemaWriter
{
    /// <summary>
    /// Creates tables and foreign keys in Oracle.
    /// </summary>
    public async Task CreateSchemaAsync(string connectionString, string schemaName, List<TableSchema> tables)
    {
        logger.LogInformation("Creating Oracle schema...");

        if (sqlCollector.IsCollecting)
        {
            sqlCollector.AddComment($"Oracle Schema: {schemaName}");
            await CreateTablesAsync(null, schemaName, tables);
            await CreateForeignKeysAsync(null, schemaName, tables);
        }
        else
        {
            await using var connection = new OracleConnection(connectionString);
            await connection.OpenAsync();

            await CreateTablesAsync(connection, schemaName, tables);
            await CreateForeignKeysAsync(connection, schemaName, tables);
        }

        logger.LogInformation("Schema creation completed");
    }

    /// <summary>
    /// Creates views in Oracle with converted SQL definitions.
    /// </summary>
    public async Task CreateViewsAsync(string connectionString, string schemaName, List<ViewSchema> views,
        List<TableSchema>? sourceTables = null)
    {
        logger.LogInformation("Creating Oracle views...");

        var tableNameMap = BuildTableNameMap(sourceTables);

        OracleConnection? connection = null;
        if (!sqlCollector.IsCollecting)
        {
            connection = new OracleConnection(connectionString);
            await connection.OpenAsync();
        }

        try
        {
            foreach (var view in views)
            {
                var viewName = QuoteIdentifier(namingConverter.Convert(view.ViewName));
                var fullViewName = $"{QuoteIdentifier(schemaName)}.{viewName}";

                var sourceDb = dialectConverter.DetectSourceDatabase(view.Definition);
                var convertedDefinition = dialectConverter.ConvertViewDefinition(
                    view.Definition,
                    sourceDb,
                    DatabaseTypes.Oracle,
                    view.SchemaName,
                    schemaName,
                    tableNameMap
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
                        await using var cmd = new OracleCommand(sql, connection);
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
    /// Creates indexes in Oracle.
    /// </summary>
    public async Task CreateIndexesAsync(string connectionString, string schemaName, List<IndexSchema> indexes)
    {
        logger.LogInformation("Creating Oracle indexes...");

        OracleConnection? connection = null;
        if (!sqlCollector.IsCollecting)
        {
            connection = new OracleConnection(connectionString);
            await connection.OpenAsync();
        }

        try
        {
            foreach (var index in indexes)
            {
                if (index.IsPrimaryKey) continue;

                var indexName = QuoteIdentifier(namingConverter.Convert(index.IndexName));
                var tableName = QuoteIdentifier(namingConverter.Convert(index.TableName));
                var fullTableName = $"{QuoteIdentifier(schemaName)}.{tableName}";
                var columns = string.Join(", ", index.Columns.Select(c => QuoteIdentifier(namingConverter.Convert(c))));

                var unique = index.IsUnique ? "UNIQUE " : "";
                var sql = $"CREATE {unique}INDEX {QuoteIdentifier(schemaName)}.{indexName} ON {fullTableName} ({columns})";

                logger.LogInformation("Creating index: {IndexName} on {Table}", indexName, tableName);

                if (sqlCollector.IsCollecting)
                {
                    sqlCollector.AddSql(sql, "Indexes", indexName);
                }
                else
                {
                    try
                    {
                        await using var cmd = new OracleCommand(sql, connection);
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
    /// Creates constraints (CHECK, UNIQUE, DEFAULT) in Oracle.
    /// </summary>
    public async Task CreateConstraintsAsync(string connectionString, string schemaName, List<ConstraintSchema> constraints)
    {
        logger.LogInformation("Creating Oracle constraints...");

        OracleConnection? connection = null;
        if (!sqlCollector.IsCollecting)
        {
            connection = new OracleConnection(connectionString);
            await connection.OpenAsync();
        }

        try
        {
            foreach (var constraint in constraints)
            {
                var constraintName = QuoteIdentifier(namingConverter.Convert(constraint.ConstraintName));
                var tableName = QuoteIdentifier(namingConverter.Convert(constraint.TableName));
                var fullTableName = $"{QuoteIdentifier(schemaName)}.{tableName}";

                string sql = constraint.Type switch
                {
                    ConstraintType.Check => GenerateCheckConstraintOracle(fullTableName, constraintName, constraint.CheckExpression!),
                    ConstraintType.Unique => GenerateUniqueConstraintOracle(fullTableName, constraintName, constraint.Columns),
                    ConstraintType.Default => GenerateDefaultConstraintOracle(fullTableName, constraint.Columns[0], constraint.DefaultExpression!),
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
                        await using var cmd = new OracleCommand(sql, connection);
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
    private async Task CreateTablesAsync(OracleConnection? connection, string schemaName, List<TableSchema> tables)
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
                await using var cmd = new OracleCommand(sql, connection);
                await cmd.ExecuteNonQueryAsync();
            }
        }
    }

    private async Task CreateForeignKeysAsync(OracleConnection? connection, string schemaName, List<TableSchema> tables)
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
                        await using var cmd = new OracleCommand(sql, connection);
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
        var quotedTableName = QuoteIdentifier(tableName);

        sb.AppendLine($"CREATE TABLE {QuoteIdentifier(schemaName)}.{quotedTableName} (");

        var columnDefinitions = table.Columns.Select(col => $"    {GenerateColumnDefinition(col)}").ToList();
        sb.AppendLine(string.Join(",\n", columnDefinitions));

        if (table.PrimaryKeys.Count > 0)
        {
            var pkColumns = string.Join(", ", table.PrimaryKeys.Select(pk => QuoteIdentifier(namingConverter.Convert(pk))));
            sb.AppendLine($",   CONSTRAINT {QuoteIdentifier($"pk_{tableName}")} PRIMARY KEY ({pkColumns})");
        }

        sb.AppendLine(")");
        return sb.ToString();
    }

    private string GenerateColumnDefinition(ColumnSchema column)
    {
        var columnName = QuoteIdentifier(namingConverter.Convert(column.ColumnName));
        var dataType = dataTypeMapper.MapDataType(column, DatabaseTypes.Oracle);
        var nullable = column.IsNullable ? "NULL" : "NOT NULL";
        var identity = column.IsIdentity ? " GENERATED BY DEFAULT AS IDENTITY" : "";

        return $"{columnName} {dataType}{identity} {nullable}";
    }

    private string GenerateAddForeignKeySql(string schemaName, TableSchema table, ForeignKeySchema fk)
    {
        var tableName = QuoteIdentifier(namingConverter.Convert(table.TableName));
        var columnName = QuoteIdentifier(namingConverter.Convert(fk.ColumnName));
        var refTable = QuoteIdentifier(namingConverter.Convert(fk.ReferencedTable));
        var refColumn = QuoteIdentifier(namingConverter.Convert(fk.ReferencedColumn));
        var fkName = QuoteIdentifier(namingConverter.Convert(fk.Name));

        return $"ALTER TABLE {QuoteIdentifier(schemaName)}.{tableName} ADD CONSTRAINT {fkName} FOREIGN KEY ({columnName}) REFERENCES {QuoteIdentifier(schemaName)}.{refTable}({refColumn})";
    }

    private static string QuoteIdentifier(string identifier) => $"\"{identifier}\"";

    private string GenerateCheckConstraintOracle(string tableName, string constraintName, string checkExpression)
    {
        var convertedExpression = ConvertCheckExpressionToOracle(checkExpression);
        return $"ALTER TABLE {tableName} ADD CONSTRAINT {constraintName} CHECK ({convertedExpression})";
    }

    private string GenerateUniqueConstraintOracle(string tableName, string constraintName, List<string> columns)
    {
        var columnList = string.Join(", ", columns.Select(c => QuoteIdentifier(namingConverter.Convert(c))));
        return $"ALTER TABLE {tableName} ADD CONSTRAINT {constraintName} UNIQUE ({columnList})";
    }

    private string GenerateDefaultConstraintOracle(string tableName, string columnName, string defaultExpression)
    {
        var convertedColumn = QuoteIdentifier(namingConverter.Convert(columnName));
        var convertedExpression = ConvertDefaultExpressionToOracle(defaultExpression);
        return $"ALTER TABLE {tableName} MODIFY {convertedColumn} DEFAULT {convertedExpression}";
    }

    private static string ConvertCheckExpressionToOracle(string expression)
    {
        var cleaned = expression.Trim();
        if (cleaned.StartsWith("CHECK", StringComparison.OrdinalIgnoreCase))
        {
            cleaned = cleaned.Substring(5).Trim();
        }
        cleaned = StripOuterParentheses(cleaned);
        // Convert SQL Server bracket-quoted identifiers [Name] to Oracle double-quoted "NAME"
        cleaned = Regex.Replace(cleaned, @"\[([^\]]+)\]", m => $"\"{m.Groups[1].Value.ToUpperInvariant()}\"");
        return cleaned;
    }
    private static string ConvertDefaultExpressionToOracle(string expression)
    {
        var cleaned = StripOuterParentheses(expression);
        cleaned = Regex.Replace(cleaned, @"\bGETDATE\(\)", "SYSDATE", RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(cleaned, @"\bNOW\(\)", "SYSDATE", RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(cleaned, @"\bCURRENT_TIMESTAMP\b", "SYSTIMESTAMP", RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(cleaned, @"\bNEWID\(\)", "SYS_GUID()", RegexOptions.IgnoreCase);
        return cleaned;
    }

    /// <summary>
    /// Strips matching outer wrapper parentheses from SQL expressions.
    /// </summary>
    private static string StripOuterParentheses(string expression)
    {
        var result = expression.Trim();
        while (result.Length >= 2 && result[0] == '(' && result[^1] == ')')
        {
            var inner = result[1..^1];
            int depth = 0;
            bool balanced = true;
            foreach (char c in inner)
            {
                if (c == '(') depth++;
                else if (c == ')')
                {
                    depth--;
                    if (depth < 0) { balanced = false; break; }
                }
            }
            if (balanced && depth == 0)
                result = inner.Trim();
            else
                break;
        }
        return result;
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