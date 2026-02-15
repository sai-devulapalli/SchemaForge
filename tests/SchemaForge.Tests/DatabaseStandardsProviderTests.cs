using SchemaForge.Abstractions.Interfaces;
using SchemaForge.Abstractions.Models;
using SchemaForge.Services;

namespace SchemaForge.Tests;

public class DatabaseStandardsProviderTests
{
    private readonly DatabaseStandardsProvider _provider = new();

    [Fact]
    public void GetStandards_PostgreSql_ReturnsCorrectStandards()
    {
        var standards = _provider.GetStandards(DatabaseTypes.PostgreSql);

        Assert.Equal("PostgreSQL", standards.DatabaseType);
        Assert.Equal(NamingConvention.SnakeCase, standards.NamingConvention);
        Assert.Equal(63, standards.MaxIdentifierLength);
        Assert.True(standards.CaseSensitive);
        Assert.Equal("\"", standards.IdentifierQuoteStart);
        Assert.Equal("\"", standards.IdentifierQuoteEnd);
        Assert.Equal("sequence", standards.IdentityStrategy);
    }

    [Fact]
    public void GetStandards_SqlServer_ReturnsCorrectStandards()
    {
        var standards = _provider.GetStandards(DatabaseTypes.SqlServer);

        Assert.Equal("SQL Server", standards.DatabaseType);
        Assert.Equal(NamingConvention.PascalCase, standards.NamingConvention);
        Assert.Equal(128, standards.MaxIdentifierLength);
        Assert.False(standards.CaseSensitive);
        Assert.Equal("[", standards.IdentifierQuoteStart);
        Assert.Equal("]", standards.IdentifierQuoteEnd);
        Assert.Equal("identity", standards.IdentityStrategy);
    }

    [Fact]
    public void GetStandards_MySql_ReturnsCorrectStandards()
    {
        var standards = _provider.GetStandards(DatabaseTypes.MySql);

        Assert.Equal("MySQL", standards.DatabaseType);
        Assert.Equal(NamingConvention.SnakeCase, standards.NamingConvention);
        Assert.Equal(64, standards.MaxIdentifierLength);
        Assert.Equal("`", standards.IdentifierQuoteStart);
        Assert.Equal("auto_increment", standards.IdentityStrategy);
    }

    [Fact]
    public void GetStandards_Oracle_ReturnsCorrectStandards()
    {
        var standards = _provider.GetStandards(DatabaseTypes.Oracle);

        Assert.Equal("Oracle", standards.DatabaseType);
        Assert.Equal(NamingConvention.Uppercase, standards.NamingConvention);
        Assert.Equal(30, standards.MaxIdentifierLength);
        Assert.Equal("\"", standards.IdentifierQuoteStart);
        Assert.Equal("sequence", standards.IdentityStrategy);
    }

    [Fact]
    public void GetStandards_AllDatabases_SupportIdentity()
    {
        Assert.True(_provider.GetStandards(DatabaseTypes.PostgreSql).SupportsIdentity);
        Assert.True(_provider.GetStandards(DatabaseTypes.SqlServer).SupportsIdentity);
        Assert.True(_provider.GetStandards(DatabaseTypes.MySql).SupportsIdentity);
        Assert.True(_provider.GetStandards(DatabaseTypes.Oracle).SupportsIdentity);
    }

    [Fact]
    public void GetStandards_AllDatabases_HaveReservedKeywords()
    {
        Assert.NotEmpty(_provider.GetStandards(DatabaseTypes.PostgreSql).ReservedKeywords);
        Assert.NotEmpty(_provider.GetStandards(DatabaseTypes.SqlServer).ReservedKeywords);
        Assert.NotEmpty(_provider.GetStandards(DatabaseTypes.MySql).ReservedKeywords);
        Assert.NotEmpty(_provider.GetStandards(DatabaseTypes.Oracle).ReservedKeywords);
    }

    [Fact]
    public void GetStandards_UnknownType_FallsBackToPostgres()
    {
        var standards = _provider.GetStandards("unknown_db");

        Assert.Equal("PostgreSQL", standards.DatabaseType);
        Assert.Equal(NamingConvention.SnakeCase, standards.NamingConvention);
    }

    [Fact]
    public void GetStandards_SchemaSupport_VariesByDatabase()
    {
        Assert.Equal("full", _provider.GetStandards(DatabaseTypes.PostgreSql).SchemaSupport);
        Assert.Equal("full", _provider.GetStandards(DatabaseTypes.SqlServer).SchemaSupport);
        Assert.Equal("database", _provider.GetStandards(DatabaseTypes.MySql).SchemaSupport);
        Assert.Equal("user", _provider.GetStandards(DatabaseTypes.Oracle).SchemaSupport);
    }
}
