using SchemaForge.Abstractions.Models;
using SchemaForge.Services;

namespace SchemaForge.Tests;

public class SqlDialectConverterTests
{
    private readonly SqlDialectConverter _converter = new();

    #region ConvertViewDefinition

    [Fact]
    public void ConvertViewDefinition_NullOrEmpty_ReturnsInput()
    {
        Assert.Null(_converter.ConvertViewDefinition(null!, "sqlserver", "postgres"));
        Assert.Equal("", _converter.ConvertViewDefinition("", "sqlserver", "postgres"));
    }

    [Fact]
    public void ConvertViewDefinition_StripsCreateViewHeader()
    {
        var input = "CREATE VIEW [dbo].[MyView] AS SELECT [Id] FROM [Users]";
        var result = _converter.ConvertViewDefinition(input, DatabaseTypes.SqlServer, DatabaseTypes.PostgreSql);

        Assert.DoesNotContain("CREATE VIEW", result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("SELECT", result);
    }

    [Fact]
    public void ConvertViewDefinition_ConvertsBracketQuotesToDoubleQuotes()
    {
        var input = "SELECT [Id], [Name] FROM [Users]";
        var result = _converter.ConvertViewDefinition(input, DatabaseTypes.SqlServer, DatabaseTypes.PostgreSql);

        Assert.Contains("\"Id\"", result);
        Assert.Contains("\"Name\"", result);
        Assert.Contains("\"Users\"", result);
        Assert.DoesNotContain("[", result);
    }

    [Fact]
    public void ConvertViewDefinition_ConvertsGetdateToNow()
    {
        var input = "SELECT GETDATE() AS CurrentDate";
        var result = _converter.ConvertViewDefinition(input, DatabaseTypes.SqlServer, DatabaseTypes.PostgreSql);

        Assert.Contains("NOW()", result);
        Assert.DoesNotContain("GETDATE", result);
    }

    [Fact]
    public void ConvertViewDefinition_ConvertsNewidToGenRandomUuid()
    {
        var input = "SELECT NEWID() AS Id";
        var result = _converter.ConvertViewDefinition(input, DatabaseTypes.SqlServer, DatabaseTypes.PostgreSql);

        Assert.Contains("gen_random_uuid()", result);
    }

    [Fact]
    public void ConvertViewDefinition_ConvertsIsnullToCoalesce()
    {
        var input = "SELECT ISNULL(Name, 'Unknown') FROM Users";
        var result = _converter.ConvertViewDefinition(input, DatabaseTypes.SqlServer, DatabaseTypes.PostgreSql);

        Assert.Contains("COALESCE(Name, 'Unknown')", result);
    }

    [Fact]
    public void ConvertViewDefinition_ConvertsBooleanLiterals()
    {
        var input = "SELECT * FROM Users WHERE IsActive = 1";
        var result = _converter.ConvertViewDefinition(input, DatabaseTypes.SqlServer, DatabaseTypes.PostgreSql);

        Assert.Contains("= TRUE", result);
    }

    [Fact]
    public void ConvertViewDefinition_ReplacesSchemaReferences()
    {
        var input = "SELECT * FROM Users";
        var result = _converter.ConvertViewDefinition(input, DatabaseTypes.SqlServer, DatabaseTypes.PostgreSql,
            sourceSchema: "dbo", targetSchema: "public");

        // Schema references in the SQL should be replaced
        Assert.DoesNotContain("dbo.", result);
    }

    [Fact]
    public void ConvertViewDefinition_ReplacesTableNames()
    {
        var tableNameMap = new Dictionary<string, string> { { "Users", "users" } };
        var input = "SELECT * FROM Users";
        var result = _converter.ConvertViewDefinition(input, DatabaseTypes.SqlServer, DatabaseTypes.PostgreSql,
            tableNameMap: tableNameMap);

        Assert.Contains("\"users\"", result);
    }

    [Fact]
    public void ConvertViewDefinition_SqlServerToMySql_ConvertsFunctions()
    {
        var input = "SELECT GETDATE(), ISNULL(x, 0)";
        var result = _converter.ConvertViewDefinition(input, DatabaseTypes.SqlServer, DatabaseTypes.MySql);

        Assert.Contains("NOW()", result);
        Assert.Contains("IFNULL(x, 0)", result);
    }

    [Fact]
    public void ConvertViewDefinition_SqlServerToOracle_ConvertsFunctions()
    {
        var input = "SELECT GETDATE(), ISNULL(x, 0)";
        var result = _converter.ConvertViewDefinition(input, DatabaseTypes.SqlServer, DatabaseTypes.Oracle);

        Assert.Contains("SYSDATE", result);
        Assert.Contains("NVL(x, 0)", result);
    }

    [Fact]
    public void ConvertViewDefinition_PostgresToSqlServer_ConvertsQuotes()
    {
        var input = "SELECT \"id\", \"name\" FROM \"users\"";
        var result = _converter.ConvertViewDefinition(input, DatabaseTypes.PostgreSql, DatabaseTypes.SqlServer);

        Assert.Contains("[id]", result);
        Assert.Contains("[name]", result);
    }

    [Fact]
    public void ConvertViewDefinition_ConvertsMySqlConcatToOperator()
    {
        var input = "SELECT CONCAT(first_name, ' ', last_name) FROM users";
        var result = _converter.ConvertViewDefinition(input, DatabaseTypes.MySql, DatabaseTypes.PostgreSql);

        Assert.Contains("||", result);
        Assert.DoesNotContain("CONCAT", result);
    }

    [Fact]
    public void ConvertViewDefinition_ConvertsPlusOperatorToBarBar()
    {
        var input = "SELECT first_name + ' ' + last_name FROM users";
        var result = _converter.ConvertViewDefinition(input, DatabaseTypes.SqlServer, DatabaseTypes.PostgreSql);

        Assert.Contains("||", result);
    }

    #endregion

    #region ConvertCheckExpression

    [Fact]
    public void ConvertCheckExpression_NullOrEmpty_ReturnsInput()
    {
        Assert.Null(_converter.ConvertCheckExpression(null!, "sqlserver", "postgres"));
        Assert.Equal("", _converter.ConvertCheckExpression("", "sqlserver", "postgres"));
    }

    [Fact]
    public void ConvertCheckExpression_RemovesCheckKeyword()
    {
        var result = _converter.ConvertCheckExpression("CHECK([Status] IN ('Active','Inactive'))", DatabaseTypes.SqlServer, DatabaseTypes.PostgreSql);
        Assert.DoesNotContain("CHECK", result);
        Assert.Contains("\"Status\"", result);
        Assert.Contains("IN", result);
    }

    [Fact]
    public void ConvertCheckExpression_StripsOuterParens()
    {
        var result = _converter.ConvertCheckExpression("([Status] IN ('Active','Inactive'))",
            DatabaseTypes.SqlServer, DatabaseTypes.PostgreSql);
        Assert.Contains("\"Status\"", result);
    }

    [Fact]
    public void ConvertCheckExpression_ConvertsBracketQuotes()
    {
        var result = _converter.ConvertCheckExpression("[Price] > 0",
            DatabaseTypes.SqlServer, DatabaseTypes.PostgreSql);
        Assert.Contains("\"Price\"", result);
        Assert.DoesNotContain("[", result);
    }

    [Fact]
    public void ConvertCheckExpression_SqlServerToMySql()
    {
        var result = _converter.ConvertCheckExpression("[Qty] > 0",
            DatabaseTypes.SqlServer, DatabaseTypes.MySql);
        Assert.Contains("`Qty`", result);
    }

    [Fact]
    public void ConvertCheckExpression_SqlServerToOracle()
    {
        var result = _converter.ConvertCheckExpression("[Amount] >= 0",
            DatabaseTypes.SqlServer, DatabaseTypes.Oracle);
        Assert.Contains("\"Amount\"", result);
    }

    [Fact]
    public void ConvertCheckExpression_PostgresToSqlServer()
    {
        var result = _converter.ConvertCheckExpression("\"age\" >= 18",
            DatabaseTypes.PostgreSql, DatabaseTypes.SqlServer);
        Assert.Contains("[age]", result);
    }

    [Fact]
    public void ConvertCheckExpression_ConvertsBooleanLiterals()
    {
        var result = _converter.ConvertCheckExpression("[IsActive] = 1",
            DatabaseTypes.SqlServer, DatabaseTypes.PostgreSql);
        Assert.Contains("= TRUE", result);
    }

    #endregion

    #region ConvertDefaultExpression

    [Fact]
    public void ConvertDefaultExpression_NullOrEmpty_ReturnsInput()
    {
        Assert.Null(_converter.ConvertDefaultExpression(null!, "sqlserver", "postgres"));
        Assert.Equal("", _converter.ConvertDefaultExpression("", "sqlserver", "postgres"));
    }

    [Fact]
    public void ConvertDefaultExpression_StripsParens()
    {
        var result = _converter.ConvertDefaultExpression("((0))", DatabaseTypes.SqlServer, DatabaseTypes.PostgreSql);
        Assert.Equal("0", result);
    }

    [Fact]
    public void ConvertDefaultExpression_ConvertsGetdateToNow()
    {
        var result = _converter.ConvertDefaultExpression("(GETDATE())", DatabaseTypes.SqlServer, DatabaseTypes.PostgreSql);
        Assert.Equal("NOW()", result);
    }

    [Fact]
    public void ConvertDefaultExpression_ConvertsNewidToUuid()
    {
        var result = _converter.ConvertDefaultExpression("(NEWID())", DatabaseTypes.SqlServer, DatabaseTypes.PostgreSql);
        Assert.Equal("gen_random_uuid()", result);
    }

    [Fact]
    public void ConvertDefaultExpression_ConvertsBracketIdentifiers()
    {
        var result = _converter.ConvertDefaultExpression("[dbo].[GetDefault]()", DatabaseTypes.SqlServer, DatabaseTypes.PostgreSql);
        Assert.Contains("\"dbo\"", result);
        Assert.Contains("\"GetDefault\"", result);
    }

    [Fact]
    public void ConvertDefaultExpression_PostgresToSqlServer()
    {
        var result = _converter.ConvertDefaultExpression("NOW()", DatabaseTypes.PostgreSql, DatabaseTypes.SqlServer);
        Assert.Equal("GETDATE()", result);
    }

    #endregion

    #region ConvertFilterExpression

    [Fact]
    public void ConvertFilterExpression_NullOrEmpty_ReturnsInput()
    {
        Assert.Null(_converter.ConvertFilterExpression(null!, "sqlserver", "postgres"));
        Assert.Equal("", _converter.ConvertFilterExpression("", "sqlserver", "postgres"));
    }

    [Fact]
    public void ConvertFilterExpression_ConvertsBracketToDoubleQuote()
    {
        var result = _converter.ConvertFilterExpression("[IsDeleted] = 0",
            DatabaseTypes.SqlServer, DatabaseTypes.PostgreSql);
        Assert.Contains("\"IsDeleted\"", result);
    }

    [Fact]
    public void ConvertFilterExpression_ConvertsDoubleQuoteToBracket()
    {
        var result = _converter.ConvertFilterExpression("\"is_deleted\" = FALSE",
            DatabaseTypes.PostgreSql, DatabaseTypes.SqlServer);
        Assert.Contains("[is_deleted]", result);
    }

    [Fact]
    public void ConvertFilterExpression_ConvertsFunctions()
    {
        var result = _converter.ConvertFilterExpression("[CreatedAt] > GETDATE()",
            DatabaseTypes.SqlServer, DatabaseTypes.PostgreSql);
        Assert.Contains("NOW()", result);
    }

    #endregion

    #region DetectSourceDatabase

    [Fact]
    public void DetectSourceDatabase_NullOrEmpty_DefaultsToSqlServer()
    {
        Assert.Equal(DatabaseTypes.SqlServer, _converter.DetectSourceDatabase(null!));
        Assert.Equal(DatabaseTypes.SqlServer, _converter.DetectSourceDatabase(""));
    }

    [Fact]
    public void DetectSourceDatabase_Getdate_ReturnsSqlServer()
    {
        Assert.Equal(DatabaseTypes.SqlServer, _converter.DetectSourceDatabase("SELECT GETDATE()"));
    }

    [Fact]
    public void DetectSourceDatabase_BracketQuotes_ReturnsSqlServer()
    {
        Assert.Equal(DatabaseTypes.SqlServer, _converter.DetectSourceDatabase("SELECT [Id] FROM [Users]"));
    }

    [Fact]
    public void DetectSourceDatabase_Sysdate_ReturnsOracle()
    {
        Assert.Equal(DatabaseTypes.Oracle, _converter.DetectSourceDatabase("SELECT SYSDATE FROM DUAL"));
    }

    [Fact]
    public void DetectSourceDatabase_Nvl_ReturnsOracle()
    {
        Assert.Equal(DatabaseTypes.Oracle, _converter.DetectSourceDatabase("SELECT NVL(x, 0) FROM t"));
    }

    [Fact]
    public void DetectSourceDatabase_Ifnull_ReturnsMySql()
    {
        Assert.Equal(DatabaseTypes.MySql, _converter.DetectSourceDatabase("SELECT IFNULL(x, 0)"));
    }

    [Fact]
    public void DetectSourceDatabase_Backtick_ReturnsMySql()
    {
        Assert.Equal(DatabaseTypes.MySql, _converter.DetectSourceDatabase("SELECT `id` FROM `users`"));
    }

    [Fact]
    public void DetectSourceDatabase_Now_ReturnsPostgres()
    {
        Assert.Equal(DatabaseTypes.PostgreSql, _converter.DetectSourceDatabase("SELECT NOW()"));
    }

    [Fact]
    public void DetectSourceDatabase_Coalesce_ReturnsPostgres()
    {
        Assert.Equal(DatabaseTypes.PostgreSql, _converter.DetectSourceDatabase("SELECT COALESCE(x, 0)"));
    }

    [Fact]
    public void DetectSourceDatabase_UnrecognizedSql_DefaultsToSqlServer()
    {
        Assert.Equal(DatabaseTypes.SqlServer, _converter.DetectSourceDatabase("SELECT 1 + 1"));
    }

    #endregion

    #region Error Handling

    [Fact]
    public void ConvertViewDefinition_UnsupportedDialect_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            _converter.ConvertViewDefinition("SELECT 1", "unsupported", "postgres"));
    }

    [Fact]
    public void ConvertCheckExpression_UnsupportedDialect_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            _converter.ConvertCheckExpression("x > 0", "sqlserver", "unsupported"));
    }

    #endregion
}
