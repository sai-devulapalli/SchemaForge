using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SchemaForge.Abstractions.Models;
using SchemaForge.Services;

namespace SchemaForge.Tests;

public class DataTypeMapperTests
{
    private readonly UniversalDataTypeMapper _mapper = new(NullLogger<UniversalDataTypeMapper>.Instance);

    private static ColumnSchema Col(string dataType, int? maxLength = null, byte? precision = null, int? scale = null) =>
        new() { ColumnName = "test", DataType = dataType, MaxLength = maxLength, Precision = precision, Scale = scale };

    // ================================================================
    // Integer types
    // ================================================================

    [Theory]
    [InlineData("int", "postgres", "integer")]
    [InlineData("int", "mysql", "INT")]
    [InlineData("int", "oracle", "NUMBER(10)")]
    [InlineData("int", "sqlserver", "INT")]
    [InlineData("bigint", "postgres", "bigint")]
    [InlineData("bigint", "mysql", "BIGINT")]
    [InlineData("bigint", "oracle", "NUMBER(19)")]
    [InlineData("bigint", "sqlserver", "BIGINT")]
    [InlineData("smallint", "postgres", "smallint")]
    [InlineData("tinyint", "postgres", "smallint")]
    public void MapDataType_IntegerTypes(string source, string target, string expected)
    {
        var result = _mapper.MapDataType(Col(source), target);
        Assert.Equal(expected, result);
    }

    // ================================================================
    // Boolean types
    // ================================================================

    [Theory]
    [InlineData("bit", "postgres", "boolean")]
    [InlineData("bit", "mysql", "TINYINT(1)")]
    [InlineData("bit", "oracle", "NUMBER(3)")]
    [InlineData("bit", "sqlserver", "BIT")]
    [InlineData("boolean", "postgres", "boolean")]
    [InlineData("boolean", "mysql", "TINYINT(1)")]
    public void MapDataType_BooleanTypes(string source, string target, string expected)
    {
        var result = _mapper.MapDataType(Col(source), target);
        Assert.Equal(expected, result);
    }

    // ================================================================
    // String types
    // ================================================================

    [Theory]
    [InlineData("varchar", 100, "postgres", "character varying(100)")]
    [InlineData("varchar", -1, "postgres", "text")]
    [InlineData("nvarchar", 50, "mysql", "VARCHAR(50)")]
    [InlineData("nvarchar", -1, "mysql", "TEXT")]
    [InlineData("varchar", 100, "oracle", "VARCHAR2(100)")]
    [InlineData("varchar", -1, "oracle", "CLOB")]
    [InlineData("text", null, "sqlserver", "VARCHAR(MAX)")]
    [InlineData("clob", null, "postgres", "text")]
    public void MapDataType_StringTypes(string source, int? maxLength, string target, string expected)
    {
        var result = _mapper.MapDataType(Col(source, maxLength: maxLength), target);
        Assert.Equal(expected, result);
    }

    // ================================================================
    // Decimal types with precision/scale
    // ================================================================

    [Theory]
    [InlineData("decimal", "postgres", 15, 2, "numeric(15,2)")]
    [InlineData("decimal", "mysql", 15, 2, "DECIMAL(15,2)")]
    [InlineData("decimal", "oracle", 15, 2, "NUMBER(15,2)")]
    [InlineData("decimal", "sqlserver", 15, 2, "DECIMAL(15,2)")]
    public void MapDataType_DecimalTypes(string source, string target, byte precision, int scale, string expected)
    {
        var result = _mapper.MapDataType(Col(source, precision: precision, scale: scale), target);
        Assert.Equal(expected, result);
    }

    // ================================================================
    // Date/Time types
    // ================================================================

    [Theory]
    [InlineData("datetime", "postgres", "timestamp without time zone")]
    [InlineData("datetime", "mysql", "DATETIME")]
    [InlineData("datetime", "oracle", "TIMESTAMP")]
    [InlineData("datetime", "sqlserver", "DATETIME2")]
    [InlineData("date", "postgres", "date")]
    [InlineData("date", "mysql", "DATE")]
    public void MapDataType_DateTimeTypes(string source, string target, string expected)
    {
        var result = _mapper.MapDataType(Col(source), target);
        Assert.Equal(expected, result);
    }

    // ================================================================
    // Special types
    // ================================================================

    [Theory]
    [InlineData("uniqueidentifier", "postgres", "uuid")]
    [InlineData("uniqueidentifier", "mysql", "CHAR(36)")]
    [InlineData("uniqueidentifier", "oracle", "RAW(16)")]
    [InlineData("varbinary", "postgres", "bytea")]
    [InlineData("xml", "postgres", "xml")]
    public void MapDataType_SpecialTypes(string source, string target, string expected)
    {
        var result = _mapper.MapDataType(Col(source), target);
        Assert.Equal(expected, result);
    }

    // ================================================================
    // Oracle-specific normalization
    // ================================================================

    [Theory]
    [InlineData("TIMESTAMP(6)", "postgres", "timestamp without time zone")]
    [InlineData("TIMESTAMP(6) WITH TIME ZONE", "postgres", "timestamp with time zone")]
    [InlineData("TIMESTAMP(6) WITH LOCAL TIME ZONE", "postgres", "timestamp with time zone")]
    public void MapDataType_OracleTimestampNormalization(string source, string target, string expected)
    {
        var result = _mapper.MapDataType(Col(source), target);
        Assert.Equal(expected, result);
    }

    // ================================================================
    // Unmapped types fall back to text
    // ================================================================

    [Theory]
    [InlineData("postgres", "text")]
    [InlineData("mysql", "TEXT")]
    [InlineData("oracle", "CLOB")]
    [InlineData("sqlserver", "VARCHAR(MAX)")]
    public void MapDataType_UnmappedType_FallsBackToText(string target, string expected)
    {
        var result = _mapper.MapDataType(Col("totally_unknown_type"), target);
        Assert.Equal(expected, result);
    }

    // ================================================================
    // Cross-database: Oracle NUMBER
    // ================================================================

    [Fact]
    public void MapDataType_OracleNumber_WithScale_MapsToNumeric()
    {
        var result = _mapper.MapDataType(Col("number", precision: 10, scale: 2), "postgres");
        Assert.Equal("numeric(10,2)", result);
    }

    [Fact]
    public void MapDataType_OracleNumber_WithoutScale_MapsToInteger()
    {
        var result = _mapper.MapDataType(Col("number"), "postgres");
        Assert.Equal("integer", result);
    }
}
