using Microsoft.Extensions.Logging;
using Moq;
using SchemaForge.Abstractions.Interfaces;
using SchemaForge.Abstractions.Models;
using SchemaForge.Providers.SqlServer;
using SchemaForge.Services;
using SchemaForge.Tests.Helpers;

namespace SchemaForge.Tests;

public class SqlServerSchemaWriterTests
{
    private readonly SqlCollector _sqlCollector = new(isCollecting: true);
    private readonly SnakeCaseConverter _namingConverter = TestServices.CreateSnakeCaseConverter("sqlserver");
    private readonly UniversalDataTypeMapper _dataTypeMapper = TestServices.CreateDataTypeMapper();
    private readonly SqlDialectConverter _dialectConverter = new();
    private readonly SqlServerSchemaWriter _writer;

    public SqlServerSchemaWriterTests()
    {
        _writer = new SqlServerSchemaWriter(
            Mock.Of<ILogger<SqlServerSchemaWriter>>(),
            _namingConverter,
            _dataTypeMapper,
            _dialectConverter,
            _sqlCollector);
    }

    [Fact]
    public async Task CreateSchemaAsync_CreatesTableWithBracketQuoting()
    {
        var tables = new List<TableSchema>
        {
            TestData.Table("Users", "dbo",
                columns: [TestData.Column("Id", "int", identity: true), TestData.Column("Name", "nvarchar", maxLength: 100)],
                primaryKeys: ["Id"])
        };

        await _writer.CreateSchemaAsync("", "dbo", tables);

        var tableSql = _sqlCollector.GetStatements().First(s => s.Category == "Tables").Sql;
        Assert.Contains("[dbo].[Users]", tableSql);
        Assert.Contains("[Id]", tableSql);
        Assert.Contains("IDENTITY(1,1)", tableSql);
        Assert.Contains("PK_Users", tableSql);
        Assert.Contains("CLUSTERED", tableSql);
    }

    [Fact]
    public async Task CreateSchemaAsync_NonDboSchema_CreatesSchema()
    {
        var tables = new List<TableSchema>
        {
            TestData.Table("Users", "dbo",
                columns: [TestData.Column("Id", "int")],
                primaryKeys: ["Id"])
        };

        await _writer.CreateSchemaAsync("", "custom", tables);

        var schemaStmt = _sqlCollector.GetStatements().First(s => s.Category == "Schema");
        Assert.Contains("CREATE SCHEMA [custom]", schemaStmt.Sql);
    }

    [Fact]
    public async Task CreateSchemaAsync_DboSchema_SkipsSchemaCreation()
    {
        var tables = new List<TableSchema>
        {
            TestData.Table("Users", "dbo",
                columns: [TestData.Column("Id", "int")],
                primaryKeys: ["Id"])
        };

        await _writer.CreateSchemaAsync("", "dbo", tables);

        Assert.DoesNotContain(_sqlCollector.GetStatements(), s => s.Category == "Schema");
    }

    [Fact]
    public async Task CreateSchemaAsync_ForeignKeys_GeneratesCorrectSql()
    {
        var tables = new List<TableSchema>
        {
            TestData.Table("Orders", "dbo",
                columns: [TestData.Column("Id", "int"), TestData.Column("UserId", "int")],
                primaryKeys: ["Id"],
                foreignKeys: [TestData.ForeignKey("FK_Orders_Users", "UserId", "Users", "Id")])
        };

        await _writer.CreateSchemaAsync("", "dbo", tables);

        var fkStmt = _sqlCollector.GetStatements().First(s => s.Category == "ForeignKeys");
        Assert.Contains("[FK_FkOrdersUsers]", fkStmt.Sql);
        Assert.Contains("FOREIGN KEY", fkStmt.Sql);
        Assert.Contains("REFERENCES", fkStmt.Sql);
    }

    [Fact]
    public async Task CreateIndexesAsync_CreatesNonclusteredIndex()
    {
        var indexes = new List<IndexSchema>
        {
            TestData.Index("IX_Users_Name", "Users", isUnique: false, columns: "Name")
        };

        await _writer.CreateIndexesAsync("", "dbo", indexes);

        var stmt = _sqlCollector.GetStatements().First(s => s.Category == "Indexes");
        Assert.Contains("NONCLUSTERED", stmt.Sql);
        Assert.Contains("[IxUsersName]", stmt.Sql);
    }

    [Fact]
    public async Task CreateIndexesAsync_CreatesClusteredIndex()
    {
        var indexes = new List<IndexSchema>
        {
            TestData.Index("IX_Users_Name", "Users", isClustered: true, columns: "Name")
        };

        await _writer.CreateIndexesAsync("", "dbo", indexes);

        var stmt = _sqlCollector.GetStatements().First(s => s.Category == "Indexes");
        Assert.Contains("CLUSTERED INDEX", stmt.Sql);
    }

    [Fact]
    public async Task CreateIndexesAsync_WithIncludedColumns()
    {
        var indexes = new List<IndexSchema>
        {
            TestData.IndexWithInclude("IX_Users_Name", "Users", "dbo",
                isUnique: false,
                columns: ["Name"],
                includedColumns: ["Email"])
        };

        await _writer.CreateIndexesAsync("", "dbo", indexes);

        var stmt = _sqlCollector.GetStatements().First(s => s.Category == "Indexes");
        Assert.Contains("INCLUDE", stmt.Sql);
        Assert.Contains("[Email]", stmt.Sql);
    }

    [Fact]
    public async Task CreateIndexesAsync_WithFilterExpression()
    {
        var indexes = new List<IndexSchema>
        {
            TestData.Index("IX_Users_Active", "Users", filterExpression: "[IsActive] = 1", columns: "Name")
        };

        await _writer.CreateIndexesAsync("", "dbo", indexes);

        var stmt = _sqlCollector.GetStatements().First(s => s.Category == "Indexes");
        Assert.Contains("WHERE [IsActive] = 1", stmt.Sql);
    }

    [Fact]
    public async Task CreateConstraintsAsync_CreatesCheckConstraint()
    {
        var constraints = new List<ConstraintSchema>
        {
            TestData.Constraint("CK_Users_Age", "Users", "dbo", ConstraintType.Check,
                columns: ["Age"], checkExpression: "[Age] >= 0")
        };

        await _writer.CreateConstraintsAsync("", "dbo", constraints);

        var stmt = _sqlCollector.GetStatements().First(s => s.Category == "Constraints");
        Assert.Contains("CHECK", stmt.Sql);
        Assert.Contains("[CkUsersAge]", stmt.Sql);
    }

    [Fact]
    public async Task CreateConstraintsAsync_CreatesDefaultConstraint()
    {
        var constraints = new List<ConstraintSchema>
        {
            TestData.Constraint("DF_Users_CreatedAt", "Users", "dbo", ConstraintType.Default,
                columns: ["CreatedAt"], defaultExpression: "(GETDATE())")
        };

        await _writer.CreateConstraintsAsync("", "dbo", constraints);

        var stmt = _sqlCollector.GetStatements().First(s => s.Category == "Constraints");
        Assert.Contains("DEFAULT", stmt.Sql);
        Assert.Contains("FOR [Createdat]", stmt.Sql);
    }

    [Fact]
    public async Task CreateViewsAsync_CreatesViewWithCorrectSyntax()
    {
        var views = new List<ViewSchema>
        {
            TestData.View("ActiveUsers", "dbo", "SELECT Id, Name FROM Users WHERE IsActive = 1")
        };

        await _writer.CreateViewsAsync("", "dbo", views);

        var stmt = _sqlCollector.GetStatements().First(s => s.Category == "Views");
        Assert.Contains("CREATE OR ALTER VIEW", stmt.Sql);
    }
}
