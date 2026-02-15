using Microsoft.Extensions.Logging;
using Moq;
using SchemaForge.Abstractions.Interfaces;
using SchemaForge.Abstractions.Models;
using SchemaForge.Providers.MySql;
using SchemaForge.Services;
using SchemaForge.Tests.Helpers;

namespace SchemaForge.Tests;

public class MySqlSchemaWriterTests
{
    private readonly SqlCollector _sqlCollector = new(isCollecting: true);
    private readonly SnakeCaseConverter _namingConverter = TestServices.CreateSnakeCaseConverter("mysql");
    private readonly UniversalDataTypeMapper _dataTypeMapper = TestServices.CreateDataTypeMapper();
    private readonly SqlDialectConverter _dialectConverter = new();
    private readonly MySqlSchemaWriter _writer;

    public MySqlSchemaWriterTests()
    {
        _writer = new MySqlSchemaWriter(
            Mock.Of<ILogger<MySqlSchemaWriter>>(),
            _namingConverter,
            _dataTypeMapper,
            _dialectConverter,
            _sqlCollector);
    }

    [Fact]
    public async Task CreateSchemaAsync_CreatesDatabase()
    {
        var tables = new List<TableSchema>
        {
            TestData.Table("Users", "dbo",
                columns: [TestData.Column("Id", "int", identity: true), TestData.Column("Name", "nvarchar", maxLength: 100)],
                primaryKeys: ["Id"])
        };

        await _writer.CreateSchemaAsync("", "mydb", tables);

        var schemaStmt = _sqlCollector.GetStatements().First(s => s.Category == "Schema");
        Assert.Contains("CREATE DATABASE IF NOT EXISTS `mydb`", schemaStmt.Sql);
    }

    [Fact]
    public async Task CreateSchemaAsync_CreatesTableWithBacktickQuoting()
    {
        var tables = new List<TableSchema>
        {
            TestData.Table("Users", "dbo",
                columns: [TestData.Column("Id", "int", identity: true), TestData.Column("Name", "nvarchar", maxLength: 100)],
                primaryKeys: ["Id"])
        };

        await _writer.CreateSchemaAsync("", "mydb", tables);

        var tableSql = _sqlCollector.GetStatements().First(s => s.Category == "Tables").Sql;
        Assert.Contains("`users`", tableSql);
        Assert.Contains("`id`", tableSql);
        Assert.Contains("AUTO_INCREMENT", tableSql);
        Assert.Contains("ENGINE=InnoDB", tableSql);
        Assert.Contains("utf8mb4", tableSql);
    }

    [Fact]
    public async Task CreateSchemaAsync_CreatesForeignKeys()
    {
        var tables = new List<TableSchema>
        {
            TestData.Table("Orders", "dbo",
                columns: [TestData.Column("Id", "int"), TestData.Column("UserId", "int")],
                primaryKeys: ["Id"],
                foreignKeys: [TestData.ForeignKey("FK_Orders_Users", "UserId", "Users", "Id")])
        };

        await _writer.CreateSchemaAsync("", "mydb", tables);

        var fkStmt = _sqlCollector.GetStatements().First(s => s.Category == "ForeignKeys");
        Assert.Contains("FOREIGN KEY", fkStmt.Sql);
        Assert.Contains("`fk_orders_users`", fkStmt.Sql);
    }

    [Fact]
    public async Task CreateIndexesAsync_CreatesIndex()
    {
        var indexes = new List<IndexSchema>
        {
            TestData.Index("IX_Users_Name", "Users", columns: "Name")
        };

        await _writer.CreateIndexesAsync("", "mydb", indexes);

        var stmt = _sqlCollector.GetStatements().First(s => s.Category == "Indexes");
        Assert.Contains("CREATE INDEX", stmt.Sql);
        Assert.Contains("`ix_users_name`", stmt.Sql);
        Assert.Contains("`name`", stmt.Sql);
    }

    [Fact]
    public async Task CreateIndexesAsync_CreatesUniqueIndex()
    {
        var indexes = new List<IndexSchema>
        {
            TestData.Index("IX_Users_Email", "Users", isUnique: true, columns: "Email")
        };

        await _writer.CreateIndexesAsync("", "mydb", indexes);

        var stmt = _sqlCollector.GetStatements().First(s => s.Category == "Indexes");
        Assert.Contains("CREATE UNIQUE INDEX", stmt.Sql);
    }

    [Fact]
    public async Task CreateIndexesAsync_SkipsPrimaryKeyIndexes()
    {
        var indexes = new List<IndexSchema>
        {
            TestData.Index("PK_Users", "Users", isPrimaryKey: true, columns: "Id")
        };

        await _writer.CreateIndexesAsync("", "mydb", indexes);

        Assert.DoesNotContain(_sqlCollector.GetStatements(), s => s.Category == "Indexes");
    }

    [Fact]
    public async Task CreateConstraintsAsync_CreatesCheckConstraint()
    {
        var constraints = new List<ConstraintSchema>
        {
            TestData.Constraint("CK_Users_Age", "Users", "dbo", ConstraintType.Check,
                columns: ["Age"], checkExpression: "[Age] >= 0")
        };

        await _writer.CreateConstraintsAsync("", "mydb", constraints);

        var stmt = _sqlCollector.GetStatements().First(s => s.Category == "Constraints");
        Assert.Contains("CHECK", stmt.Sql);
        Assert.Contains("`ck_users_age`", stmt.Sql);
    }

    [Fact]
    public async Task CreateConstraintsAsync_CreatesUniqueConstraint()
    {
        var constraints = new List<ConstraintSchema>
        {
            TestData.Constraint("UQ_Users_Email", "Users", "dbo", ConstraintType.Unique,
                columns: ["Email"])
        };

        await _writer.CreateConstraintsAsync("", "mydb", constraints);

        var stmt = _sqlCollector.GetStatements().First(s => s.Category == "Constraints");
        Assert.Contains("UNIQUE", stmt.Sql);
        Assert.Contains("`email`", stmt.Sql);
    }

    [Fact]
    public async Task CreateViewsAsync_CreatesViewWithCorrectSyntax()
    {
        var views = new List<ViewSchema>
        {
            TestData.View("ActiveUsers", "dbo", "SELECT [Id], [Name] FROM [Users] WHERE [IsActive] = 1")
        };

        await _writer.CreateViewsAsync("", "mydb", views);

        var stmt = _sqlCollector.GetStatements().First(s => s.Category == "Views");
        Assert.Contains("CREATE OR REPLACE VIEW", stmt.Sql);
        Assert.Contains("`active_users`", stmt.Sql);
    }
}
