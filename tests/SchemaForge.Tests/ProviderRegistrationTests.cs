using Microsoft.Extensions.DependencyInjection;
using SchemaForge.Abstractions.Configuration;
using SchemaForge.Abstractions.Interfaces;
using SchemaForge.Abstractions.Models;
using SchemaForge.Providers.Postgres;
using SchemaForge.Providers.SqlServer;
using SchemaForge.Providers.MySql;
using SchemaForge.Providers.Oracle;

namespace SchemaForge.Tests;

public class ProviderRegistrationTests
{
    [Fact]
    public void PostgresProvider_HasCorrectProviderKey()
    {
        var provider = new PostgresProvider();
        Assert.Equal(DatabaseTypes.PostgreSql, provider.ProviderKey);
    }

    [Fact]
    public void SqlServerProvider_HasCorrectProviderKey()
    {
        var provider = new SqlServerProvider();
        Assert.Equal(DatabaseTypes.SqlServer, provider.ProviderKey);
    }

    [Fact]
    public void MySqlProvider_HasCorrectProviderKey()
    {
        var provider = new MySqlProvider();
        Assert.Equal(DatabaseTypes.MySql, provider.ProviderKey);
    }

    [Fact]
    public void OracleProvider_HasCorrectProviderKey()
    {
        var provider = new OracleProvider();
        Assert.Equal(DatabaseTypes.Oracle, provider.ProviderKey);
    }

    [Theory]
    [InlineData(typeof(PostgresProvider), "postgres")]
    [InlineData(typeof(SqlServerProvider), "sqlserver")]
    [InlineData(typeof(MySqlProvider), "mysql")]
    [InlineData(typeof(OracleProvider), "oracle")]
    public void Register_AddsKeyedSchemaReader(Type providerType, string providerKey)
    {
        var services = new ServiceCollection();
        AddLoggingAndDependencies(services);

        var provider = (IDatabaseProvider)Activator.CreateInstance(providerType)!;
        provider.Register(services);

        var sp = services.BuildServiceProvider();
        var reader = sp.GetKeyedService<ISchemaReader>(providerKey);
        Assert.NotNull(reader);
    }

    [Theory]
    [InlineData(typeof(PostgresProvider), "postgres")]
    [InlineData(typeof(SqlServerProvider), "sqlserver")]
    [InlineData(typeof(MySqlProvider), "mysql")]
    [InlineData(typeof(OracleProvider), "oracle")]
    public void Register_AddsKeyedSchemaWriter(Type providerType, string providerKey)
    {
        var services = new ServiceCollection();
        AddLoggingAndDependencies(services);

        var provider = (IDatabaseProvider)Activator.CreateInstance(providerType)!;
        provider.Register(services);

        var sp = services.BuildServiceProvider();
        var writer = sp.GetKeyedService<ISchemaWriter>(providerKey);
        Assert.NotNull(writer);
    }

    private static void AddLoggingAndDependencies(IServiceCollection services)
    {
        services.AddLogging();
        services.AddSingleton<IDatabaseStandardsProvider, SchemaForge.Services.DatabaseStandardsProvider>();
        services.Configure<MigrationSettings>(s => {
            s.TargetDatabaseType = "postgres";
            s.NamingConvention = "auto";
            s.UseTargetDatabaseStandards = true;
        });
        services.AddSingleton<INamingConverter, SchemaForge.Services.SnakeCaseConverter>();
        services.AddSingleton<IDataTypeMapper, SchemaForge.Services.UniversalDataTypeMapper>();
        services.AddSingleton<ISqlDialectConverter, SchemaForge.Services.SqlDialectConverter>();
        services.AddSingleton<ISqlCollector>(new SchemaForge.Services.SqlCollector(false));
    }
}
