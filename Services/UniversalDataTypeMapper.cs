using SchemaForge.Services.Interfaces;
using SchemaForge.Models;
using Microsoft.Extensions.Logging;

namespace SchemaForge.Services;

/// <summary>
/// Maps data types between any supported database platforms.
/// Handles type conversion for PostgreSQL, MySQL, Oracle, and SQL Server.
/// Logs warnings for unmapped source types that fall through to defaults.
/// </summary>
public class UniversalDataTypeMapper(ILogger<UniversalDataTypeMapper> logger) : IDataTypeMapper
{
    // Track warned types to avoid spamming logs
    private readonly HashSet<string> _warnedTypes = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Maps a column's data type to the equivalent in the target database.
    /// Logs a warning if the source type is not explicitly mapped.
    /// </summary>
    public string MapDataType(ColumnSchema column, string targetDatabase)
    {
        var result = targetDatabase.ToLowerInvariant() switch
        {
            DatabaseTypes.PostgreSql => MapToPostgreSqlType(column),
            DatabaseTypes.MySql => MapToMySqlType(column),
            DatabaseTypes.Oracle => MapToOracleType(column),
            DatabaseTypes.SqlServer => MapToSqlServerType(column),
            _ => MapToPostgreSqlType(column) // Default
        };

        return result;
    }

    /// <summary>
    /// Logs a warning for unmapped data types (only once per type to avoid log spam).
    /// </summary>
    private void WarnUnmappedType(string sourceType, string targetDb, string fallbackType)
    {
        var key = $"{sourceType}:{targetDb}";
        if (_warnedTypes.Add(key))
        {
            logger.LogWarning("Unmapped data type '{SourceType}' for target '{TargetDb}', using fallback '{FallbackType}'",
                sourceType, targetDb, fallbackType);
        }
    }

    /// <summary>Maps any source type to PostgreSQL equivalent.</summary>
    private string MapToPostgreSqlType(ColumnSchema column)
    {
        var mapped = column.DataType.ToLowerInvariant() switch
        {
            "int" or "integer" => "integer",
            "bigint" => "bigint",
            "smallint" => "smallint",
            "tinyint" => "smallint",
            "bit" or "boolean" => "boolean",
            "decimal" or "numeric" => $"numeric({column.Precision ?? 18},{column.Scale ?? 0})",
            "money" or "smallmoney" => "numeric(19,4)",
            "float" or "double precision" => "double precision",
            "real" => "real",
            "datetime" or "datetime2" or "smalldatetime" => "timestamp without time zone",
            "date" => "date",
            "time" => "time without time zone",
            "datetimeoffset" or "timestamp with time zone" => "timestamp with time zone",
            "char" or "character" => $"character({column.MaxLength ?? 1})",
            "varchar" or "character varying" => column.MaxLength == -1 ? "text" : $"character varying({column.MaxLength})",
            "nchar" => $"character({column.MaxLength ?? 1})",
            "nvarchar" => column.MaxLength == -1 ? "text" : $"character varying({column.MaxLength})",
            "text" or "ntext" or "clob" => "text",
            "uniqueidentifier" or "uuid" => "uuid",
            "varbinary" or "binary" or "image" or "blob" or "raw" => "bytea",
            "bytea" => "bytea",
            "xml" or "xmltype" => "xml",
            "varchar2" => column.MaxLength == -1 ? "text" : $"character varying({column.MaxLength})",
            "number" => column.Scale.HasValue && column.Scale > 0
                ? $"numeric({column.Precision ?? 18},{column.Scale})"
                : "integer",
            "timestamp" => "timestamp without time zone",
            "json" => "json",
            "jsonb" => "jsonb",
            "geography" or "geometry" => "text",
            "hierarchyid" => "text",
            "sql_variant" => "text",
            "sysname" => "character varying(128)",
            _ => (string?)null
        };

        if (mapped is null)
        {
            WarnUnmappedType(column.DataType, "PostgreSQL", "text");
            return "text";
        }
        return mapped;
    }

    /// <summary>Maps any source type to MySQL equivalent.</summary>
    private string MapToMySqlType(ColumnSchema column)
    {
        var mapped = column.DataType.ToLowerInvariant() switch
        {
            "int" or "integer" => "INT",
            "bigint" => "BIGINT",
            "smallint" => "SMALLINT",
            "tinyint" or "boolean" or "bit" => "TINYINT(1)",
            "decimal" or "numeric" => $"DECIMAL({column.Precision ?? 18},{column.Scale ?? 0})",
            "money" or "smallmoney" => "DECIMAL(19,4)",
            "float" or "double precision" => "DOUBLE",
            "real" => "FLOAT",
            "datetime" or "datetime2" or "timestamp" or "smalldatetime" => "DATETIME",
            "date" => "DATE",
            "time" => "TIME",
            "datetimeoffset" or "timestamp with time zone" => "DATETIME",
            "char" or "character" => $"CHAR({column.MaxLength ?? 1})",
            "varchar" or "character varying" => column.MaxLength == -1 ? "TEXT" : $"VARCHAR({column.MaxLength})",
            "nchar" => $"CHAR({column.MaxLength ?? 1})",
            "nvarchar" => column.MaxLength == -1 ? "TEXT" : $"VARCHAR({column.MaxLength})",
            "text" or "ntext" or "clob" => "TEXT",
            "uuid" or "uniqueidentifier" => "CHAR(36)",
            "bytea" or "varbinary" or "binary" or "image" or "blob" => "BLOB",
            "xml" or "xmltype" => "TEXT",
            "number" => column.Scale.HasValue && column.Scale > 0
                ? $"DECIMAL({column.Precision ?? 18},{column.Scale})"
                : "INT",
            "varchar2" => column.MaxLength == -1 ? "TEXT" : $"VARCHAR({column.MaxLength})",
            "raw" => "BLOB",
            "json" or "jsonb" => "JSON",
            "geography" or "geometry" => "TEXT",
            "hierarchyid" => "VARCHAR(4000)",
            "sql_variant" => "TEXT",
            "sysname" => "VARCHAR(128)",
            _ => (string?)null
        };

        if (mapped is null)
        {
            WarnUnmappedType(column.DataType, "MySQL", "TEXT");
            return "TEXT";
        }
        return mapped;
    }

    /// <summary>Maps any source type to Oracle equivalent.</summary>
    private string MapToOracleType(ColumnSchema column)
    {
        var mapped = column.DataType.ToLowerInvariant() switch
        {
            "int" or "integer" => "NUMBER(10)",
            "bigint" => "NUMBER(19)",
            "smallint" => "NUMBER(5)",
            "tinyint" or "boolean" or "bit" => "NUMBER(3)",
            "decimal" or "numeric" => $"NUMBER({column.Precision ?? 18},{column.Scale ?? 0})",
            "money" or "smallmoney" => "NUMBER(19,4)",
            "float" or "double precision" or "real" => "BINARY_DOUBLE",
            "datetime" or "datetime2" or "timestamp" or "smalldatetime" => "TIMESTAMP",
            "date" => "DATE",
            "time" => "TIMESTAMP",
            "datetimeoffset" or "timestamp with time zone" => "TIMESTAMP WITH TIME ZONE",
            "char" or "character" => $"CHAR({column.MaxLength ?? 1})",
            "varchar" or "character varying" => column.MaxLength == -1 ? "CLOB" : $"VARCHAR2({column.MaxLength})",
            "nchar" => $"CHAR({column.MaxLength ?? 1})",
            "nvarchar" => column.MaxLength == -1 ? "CLOB" : $"VARCHAR2({column.MaxLength})",
            "text" or "ntext" or "clob" => "CLOB",
            "uuid" or "uniqueidentifier" => "RAW(16)",
            "bytea" or "varbinary" or "binary" or "image" or "blob" => "BLOB",
            "xml" or "xmltype" => "XMLTYPE",
            "number" => column.Scale.HasValue && column.Scale > 0
                ? $"NUMBER({column.Precision ?? 18},{column.Scale})"
                : "NUMBER(10)",
            "varchar2" => column.MaxLength == -1 ? "CLOB" : $"VARCHAR2({column.MaxLength})",
            "raw" => $"RAW({column.MaxLength ?? 2000})",
            "json" or "jsonb" => "CLOB",
            "geography" or "geometry" => "CLOB",
            "hierarchyid" => "VARCHAR2(4000)",
            "sql_variant" => "CLOB",
            "sysname" => "VARCHAR2(128)",
            _ => (string?)null
        };

        if (mapped is null)
        {
            WarnUnmappedType(column.DataType, "Oracle", "CLOB");
            return "CLOB";
        }
        return mapped;
    }

    /// <summary>Maps any source type to SQL Server equivalent.</summary>
    private string MapToSqlServerType(ColumnSchema column)
    {
        var mapped = column.DataType.ToLowerInvariant() switch
        {
            "int" or "integer" => "INT",
            "bigint" => "BIGINT",
            "smallint" => "SMALLINT",
            "tinyint" => "TINYINT",
            "boolean" or "bit" => "BIT",
            "decimal" or "numeric" => $"DECIMAL({column.Precision ?? 18},{column.Scale ?? 0})",
            "money" => "MONEY",
            "smallmoney" => "SMALLMONEY",
            "float" or "double precision" => "FLOAT",
            "real" => "REAL",
            "datetime" or "timestamp" or "timestamp without time zone" => "DATETIME2",
            "timestamp with time zone" or "datetimeoffset" => "DATETIMEOFFSET",
            "date" => "DATE",
            "time" or "time without time zone" => "TIME",
            "char" or "character" => $"CHAR({column.MaxLength ?? 1})",
            "varchar" or "character varying" => column.MaxLength == -1 ? "VARCHAR(MAX)" : $"VARCHAR({column.MaxLength})",
            "nchar" => $"NCHAR({column.MaxLength ?? 1})",
            "nvarchar" => column.MaxLength == -1 ? "NVARCHAR(MAX)" : $"NVARCHAR({column.MaxLength})",
            "text" or "clob" => "VARCHAR(MAX)",
            "ntext" => "NVARCHAR(MAX)",
            "uuid" or "uniqueidentifier" => "UNIQUEIDENTIFIER",
            "bytea" or "varbinary" or "binary" or "blob" => "VARBINARY(MAX)",
            "image" => "IMAGE",
            "xml" or "xmltype" => "XML",
            "number" => column.Scale.HasValue && column.Scale > 0
                ? $"DECIMAL({column.Precision ?? 18},{column.Scale})"
                : "INT",
            "varchar2" => column.MaxLength == -1 ? "VARCHAR(MAX)" : $"VARCHAR({column.MaxLength})",
            "raw" => "VARBINARY(MAX)",
            "json" or "jsonb" => "NVARCHAR(MAX)",
            "geography" => "GEOGRAPHY",
            "geometry" => "GEOMETRY",
            "hierarchyid" => "HIERARCHYID",
            "sql_variant" => "SQL_VARIANT",
            "sysname" => "SYSNAME",
            _ => (string?)null
        };

        if (mapped is null)
        {
            WarnUnmappedType(column.DataType, "SQL Server", "VARCHAR(MAX)");
            return "VARCHAR(MAX)";
        }
        return mapped;
    }
}
