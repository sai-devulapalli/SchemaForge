using Microsoft.Extensions.Options;
using SchemaForge.Abstractions.Configuration;
using SchemaForge.Services;

namespace SchemaForge.Tests;

public class NamingConverterTests
{
    private static SnakeCaseConverter CreateConverter(
        string targetDb = "postgres",
        string namingConvention = "auto",
        bool preserveSourceCase = false,
        bool useTargetDbStandards = true)
    {
        var settings = Options.Create(new MigrationSettings
        {
            TargetDatabaseType = targetDb,
            NamingConvention = namingConvention,
            PreserveSourceCase = preserveSourceCase,
            UseTargetDatabaseStandards = useTargetDbStandards
        });
        return new SnakeCaseConverter(settings, new DatabaseStandardsProvider());
    }

    // ================================================================
    // Auto mode: target-specific conventions
    // ================================================================

    [Theory]
    [InlineData("OrderHeaders", "postgres", "order_headers")]
    [InlineData("OrderHeaders", "mysql", "order_headers")]
    [InlineData("order_headers", "sqlserver", "OrderHeaders")]
    [InlineData("order_headers", "oracle", "ORDER_HEADERS")]
    public void Convert_AutoMode_UsesTargetConvention(string input, string targetDb, string expected)
    {
        var converter = CreateConverter(targetDb: targetDb);
        Assert.Equal(expected, converter.Convert(input));
    }

    // ================================================================
    // Explicit naming convention
    // ================================================================

    [Theory]
    [InlineData("OrderHeaders", "snake_case", "order_headers")]
    [InlineData("order_headers", "PascalCase", "OrderHeaders")]
    [InlineData("order_headers", "camelCase", "orderHeaders")]
    [InlineData("OrderHeaders", "lowercase", "orderheaders")]
    [InlineData("OrderHeaders", "UPPERCASE", "ORDERHEADERS")]
    public void Convert_ExplicitConvention(string input, string convention, string expected)
    {
        var converter = CreateConverter(namingConvention: convention);
        Assert.Equal(expected, converter.Convert(input));
    }

    // ================================================================
    // Preserve mode
    // ================================================================

    [Fact]
    public void Convert_PreserveConvention_KeepsOriginal()
    {
        var converter = CreateConverter(namingConvention: "preserve");
        Assert.Equal("MyTable_Name", converter.Convert("MyTable_Name"));
    }

    [Fact]
    public void Convert_PreserveSourceCase_KeepsOriginal()
    {
        var converter = CreateConverter(preserveSourceCase: true);
        Assert.Equal("MyTable_Name", converter.Convert("MyTable_Name"));
    }

    // ================================================================
    // Edge cases for snake_case conversion
    // ================================================================

    [Theory]
    [InlineData("XMLParser", "xml_parser")]
    [InlineData("ID", "id")]
    [InlineData("UserID", "user_id")]
    [InlineData("already_snake", "already_snake")]
    [InlineData("A", "a")]
    [InlineData("", "")]
    public void Convert_SnakeCase_EdgeCases(string input, string expected)
    {
        var converter = CreateConverter(targetDb: "postgres");
        Assert.Equal(expected, converter.Convert(input));
    }

    // ================================================================
    // Edge cases for PascalCase conversion
    // ================================================================

    [Theory]
    [InlineData("order_headers", "OrderHeaders")]
    [InlineData("user_id", "UserId")]
    [InlineData("a", "A")]
    public void Convert_PascalCase_EdgeCases(string input, string expected)
    {
        var converter = CreateConverter(targetDb: "sqlserver");
        Assert.Equal(expected, converter.Convert(input));
    }

    // ================================================================
    // Null/empty input
    // ================================================================

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Convert_NullOrEmpty_ReturnsInput(string? input)
    {
        var converter = CreateConverter();
        Assert.Equal(input, converter.Convert(input!));
    }
}
