using SchemaForge.Services.Interfaces;
using SchemaForge.Models;

namespace SchemaForge.Services;

/// <summary>
/// Maps SQL Server data types to PostgreSQL equivalents.
/// This is a specialized mapper for SQL Server to PostgreSQL migrations.
/// For universal mapping, use UniversalDataTypeMapper instead.
/// </summary>
public class SqlServerToPostgresMapper : IDataTypeMapper
{
    /// <summary>
    /// Maps a SQL Server column type to PostgreSQL type.
    /// </summary>
    public string MapDataType(ColumnSchema column,string targetDatabase) => column.DataType.ToLowerInvariant() switch
    {
        "int" => "integer",
        "bigint" => "bigint",
        "smallint" => "smallint",
        "tinyint" => "smallint",
        "bit" => "boolean",
        "decimal" or "numeric" => $"numeric({column.Precision ?? 18},{column.Scale ?? 0})",
        "money" or "smallmoney" => "numeric(19,4)",
        "float" => "double precision",
        "real" => "real",
        "datetime" or "datetime2" or "smalldatetime" => "timestamp without time zone",
        "date" => "date",
        "time" => "time without time zone",
        "datetimeoffset" => "timestamp with time zone",
        "char" => $"character({column.MaxLength ?? 1})",
        "varchar" => column.MaxLength == -1 ? "text" : $"character varying({column.MaxLength})",
        "nchar" => $"character({column.MaxLength ?? 1})",
        "nvarchar" => column.MaxLength == -1 ? "text" : $"character varying({column.MaxLength})",
        "text" or "ntext" => "text",
        "uniqueidentifier" => "uuid",
        "varbinary" or "binary" or "image" => "bytea",
        "xml" => "xml",
        _ => "text"
    };
}