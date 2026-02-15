using SchemaForge.Abstractions.Models;
using SchemaForge.Builder;

namespace SchemaForge.Tests;

public class DbMigrateTests
{
    [Fact]
    public void FromSqlServer_SetsCorrectType()
    {
        var builder = DbMigrate.FromSqlServer("Server=localhost");
        var settings = builder.GetSettings();

        Assert.Equal(DatabaseTypes.SqlServer, settings.SourceDatabaseType);
        Assert.Equal("Server=localhost", settings.SourceConnectionString);
    }

    [Fact]
    public void FromPostgres_SetsCorrectType()
    {
        var builder = DbMigrate.FromPostgres("Host=localhost");
        var settings = builder.GetSettings();

        Assert.Equal(DatabaseTypes.PostgreSql, settings.SourceDatabaseType);
        Assert.Equal("Host=localhost", settings.SourceConnectionString);
    }

    [Fact]
    public void FromMySql_SetsCorrectType()
    {
        var builder = DbMigrate.FromMySql("Server=localhost");
        var settings = builder.GetSettings();

        Assert.Equal(DatabaseTypes.MySql, settings.SourceDatabaseType);
        Assert.Equal("Server=localhost", settings.SourceConnectionString);
    }

    [Fact]
    public void FromOracle_SetsCorrectType()
    {
        var builder = DbMigrate.FromOracle("Data Source=localhost");
        var settings = builder.GetSettings();

        Assert.Equal(DatabaseTypes.Oracle, settings.SourceDatabaseType);
        Assert.Equal("Data Source=localhost", settings.SourceConnectionString);
    }

    [Fact]
    public void From_LowercasesType()
    {
        var builder = DbMigrate.From("SQLSERVER", "Server=localhost");
        var settings = builder.GetSettings();

        Assert.Equal("sqlserver", settings.SourceDatabaseType);
    }

    [Fact]
    public void Create_ReturnsEmptyBuilder()
    {
        var builder = DbMigrate.Create();
        var settings = builder.GetSettings();

        Assert.Equal(string.Empty, settings.SourceConnectionString);
        Assert.Equal(string.Empty, settings.TargetConnectionString);
    }

    [Fact]
    public void From_SetsConnectionString()
    {
        var builder = DbMigrate.From("postgres", "Host=db.example.com");
        var settings = builder.GetSettings();

        Assert.Equal("Host=db.example.com", settings.SourceConnectionString);
    }
}
