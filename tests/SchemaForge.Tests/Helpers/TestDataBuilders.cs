using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using SchemaForge.Abstractions.Configuration;
using SchemaForge.Abstractions.Interfaces;
using SchemaForge.Abstractions.Models;
using SchemaForge.Services;

namespace SchemaForge.Tests.Helpers;

/// <summary>
/// Factory methods for creating properly-configured service instances for tests.
/// </summary>
public static class TestServices
{
    public static SnakeCaseConverter CreateSnakeCaseConverter(string targetDb = "postgres")
    {
        var settings = Options.Create(new MigrationSettings
        {
            TargetDatabaseType = targetDb,
            NamingConvention = "auto",
            UseTargetDatabaseStandards = true
        });
        return new SnakeCaseConverter(settings, new DatabaseStandardsProvider());
    }

    public static UniversalDataTypeMapper CreateDataTypeMapper()
        => new(Mock.Of<ILogger<UniversalDataTypeMapper>>());

    public static TableDependencySorter CreateDependencySorter()
        => new(Mock.Of<ILogger<TableDependencySorter>>());
}

/// <summary>
/// Convenience factory methods for building test schema objects.
/// </summary>
public static class TestData
{
    public static TableSchema Table(string name, string schema = "dbo", params ColumnSchema[] columns)
        => new()
        {
            TableName = name,
            SchemaName = schema,
            Columns = columns.ToList()
        };

    public static TableSchema Table(string name, string schema, List<ColumnSchema> columns,
        List<string>? primaryKeys = null,
        List<ForeignKeySchema>? foreignKeys = null,
        List<IndexSchema>? indexes = null,
        List<ConstraintSchema>? constraints = null)
        => new()
        {
            TableName = name,
            SchemaName = schema,
            Columns = columns,
            PrimaryKeys = primaryKeys ?? [],
            ForeignKeys = foreignKeys ?? [],
            Indexes = indexes ?? [],
            Constraints = constraints ?? []
        };

    public static ColumnSchema Column(string name, string dataType,
        bool nullable = false, bool identity = false,
        int? maxLength = null, byte? precision = null, int? scale = null,
        string? defaultValue = null)
        => new()
        {
            ColumnName = name,
            DataType = dataType,
            IsNullable = nullable,
            IsIdentity = identity,
            MaxLength = maxLength,
            Precision = precision,
            Scale = scale,
            DefaultValue = defaultValue
        };

    public static ForeignKeySchema ForeignKey(string name, string column,
        string refTable, string refColumn, string refSchema = "dbo")
        => new()
        {
            Name = name,
            ColumnName = column,
            ReferencedTable = refTable,
            ReferencedColumn = refColumn,
            ReferencedSchema = refSchema
        };

    public static IndexSchema Index(string name, string table, string schema = "dbo",
        bool isUnique = false, bool isClustered = false, bool isPrimaryKey = false,
        string? filterExpression = null, params string[] columns)
        => new()
        {
            IndexName = name,
            TableName = table,
            SchemaName = schema,
            IsUnique = isUnique,
            IsClustered = isClustered,
            IsPrimaryKey = isPrimaryKey,
            FilterExpression = filterExpression,
            Columns = columns.ToList()
        };

    public static IndexSchema IndexWithInclude(string name, string table, string schema,
        bool isUnique, string[] columns, string[] includedColumns)
        => new()
        {
            IndexName = name,
            TableName = table,
            SchemaName = schema,
            IsUnique = isUnique,
            IsClustered = false,
            IsPrimaryKey = false,
            Columns = columns.ToList(),
            IncludedColumns = includedColumns.ToList()
        };

    public static ViewSchema View(string name, string schema, string definition)
        => new()
        {
            ViewName = name,
            SchemaName = schema,
            Definition = definition
        };

    public static ConstraintSchema Constraint(string name, string table, string schema,
        ConstraintType type, List<string> columns,
        string? checkExpression = null, string? defaultExpression = null,
        string? columnDataType = null)
        => new()
        {
            ConstraintName = name,
            TableName = table,
            SchemaName = schema,
            Type = type,
            Columns = columns,
            CheckExpression = checkExpression,
            DefaultExpression = defaultExpression,
            ColumnDataType = columnDataType
        };
}
