using SchemaForge.Services;

namespace SchemaForge.Tests;

public class SqlCollectorTests
{
    [Fact]
    public void IsCollecting_WhenTrue_ReturnsTrue()
    {
        var collector = new SqlCollector(isCollecting: true);
        Assert.True(collector.IsCollecting);
    }

    [Fact]
    public void IsCollecting_WhenFalse_ReturnsFalse()
    {
        var collector = new SqlCollector(isCollecting: false);
        Assert.False(collector.IsCollecting);
    }

    [Fact]
    public void AddSql_WhenCollecting_AddsStatement()
    {
        var collector = new SqlCollector(isCollecting: true);
        collector.AddSql("CREATE TABLE foo (id INT)", "Tables", "foo");

        var statements = collector.GetStatements();
        Assert.Single(statements);
        Assert.Equal("CREATE TABLE foo (id INT)", statements[0].Sql);
        Assert.Equal("Tables", statements[0].Category);
        Assert.Equal("foo", statements[0].ObjectName);
    }

    [Fact]
    public void AddSql_WhenNotCollecting_DoesNotAddStatement()
    {
        var collector = new SqlCollector(isCollecting: false);
        collector.AddSql("CREATE TABLE foo (id INT)", "Tables", "foo");

        Assert.Empty(collector.GetStatements());
    }

    [Fact]
    public void AddSql_TrimsSqlWhitespace()
    {
        var collector = new SqlCollector(isCollecting: true);
        collector.AddSql("  SELECT 1  ", "Query");

        Assert.Equal("SELECT 1", collector.GetStatements()[0].Sql);
    }

    [Fact]
    public void AddComment_WhenCollecting_AddsComment()
    {
        var collector = new SqlCollector(isCollecting: true);
        collector.AddComment("This is a comment");

        var statements = collector.GetStatements();
        Assert.Single(statements);
        Assert.Equal("-- This is a comment", statements[0].Sql);
        Assert.Equal("Comment", statements[0].Category);
    }

    [Fact]
    public void AddComment_WhenNotCollecting_DoesNotAdd()
    {
        var collector = new SqlCollector(isCollecting: false);
        collector.AddComment("This is a comment");

        Assert.Empty(collector.GetStatements());
    }

    [Fact]
    public void AddComment_WhenIncludeCommentsFalse_DoesNotAdd()
    {
        var collector = new SqlCollector(isCollecting: true, includeComments: false);
        collector.AddComment("This is a comment");

        Assert.Empty(collector.GetStatements());
    }

    [Fact]
    public void GetStatements_ReturnsReadOnlyList()
    {
        var collector = new SqlCollector(isCollecting: true);
        var statements = collector.GetStatements();
        Assert.IsAssignableFrom<IReadOnlyList<Abstractions.Models.SqlStatement>>(statements);
    }

    [Fact]
    public void GetScript_ContainsHeader()
    {
        var collector = new SqlCollector(isCollecting: true);
        var script = collector.GetScript();

        Assert.Contains("SchemaForge Dry Run SQL Script", script);
        Assert.Contains("Generated:", script);
    }

    [Fact]
    public void GetScript_GroupsByCategory()
    {
        var collector = new SqlCollector(isCollecting: true);
        collector.AddSql("CREATE TABLE a (id INT)", "Tables", "a");
        collector.AddSql("CREATE INDEX idx ON a(id)", "Indexes", "idx");

        var script = collector.GetScript();
        Assert.Contains("=== Tables ===", script);
        Assert.Contains("=== Indexes ===", script);
    }

    [Fact]
    public void GetScript_AppendsSemicolonWhenMissing()
    {
        var collector = new SqlCollector(isCollecting: true);
        collector.AddSql("SELECT 1", "Query");

        var script = collector.GetScript();
        Assert.Contains("SELECT 1\n;\n", script);
    }

    [Fact]
    public void Clear_RemovesAllStatements()
    {
        var collector = new SqlCollector(isCollecting: true);
        collector.AddSql("SELECT 1", "Query");
        collector.AddComment("A comment");

        collector.Clear();
        Assert.Empty(collector.GetStatements());
    }
}
