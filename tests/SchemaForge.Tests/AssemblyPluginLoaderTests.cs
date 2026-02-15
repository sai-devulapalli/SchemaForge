using Microsoft.Extensions.DependencyInjection;
using SchemaForge.Abstractions.Configuration;
using SchemaForge.Abstractions.Interfaces;
using SchemaForge.Services;

namespace SchemaForge.Tests;

public class AssemblyPluginLoaderTests
{
    [Fact]
    public void LoadProviders_DiscoversAllFourProviders()
    {
        var services = new ServiceCollection();
        AddRequiredDependencies(services);

        var loader = new AssemblyPluginLoader();
        loader.LoadProviders(services);

        var sp = services.BuildServiceProvider();

        // All 4 providers should register keyed ISchemaReader services
        Assert.NotNull(sp.GetKeyedService<ISchemaReader>("sqlserver"));
        Assert.NotNull(sp.GetKeyedService<ISchemaReader>("postgres"));
        Assert.NotNull(sp.GetKeyedService<ISchemaReader>("mysql"));
        Assert.NotNull(sp.GetKeyedService<ISchemaReader>("oracle"));
    }

    [Fact]
    public void LoadProviders_RegistersKeyedSchemaWriters()
    {
        var services = new ServiceCollection();
        AddRequiredDependencies(services);

        var loader = new AssemblyPluginLoader();
        loader.LoadProviders(services);

        var sp = services.BuildServiceProvider();

        Assert.NotNull(sp.GetKeyedService<ISchemaWriter>("sqlserver"));
        Assert.NotNull(sp.GetKeyedService<ISchemaWriter>("postgres"));
        Assert.NotNull(sp.GetKeyedService<ISchemaWriter>("mysql"));
        Assert.NotNull(sp.GetKeyedService<ISchemaWriter>("oracle"));
    }

    [Fact]
    public void LoadProviders_CanBeCalledMultipleTimes()
    {
        var services = new ServiceCollection();
        AddRequiredDependencies(services);

        var loader = new AssemblyPluginLoader();
        loader.LoadProviders(services);
        loader.LoadProviders(services); // Call again - should not throw

        var sp = services.BuildServiceProvider();
        Assert.NotNull(sp.GetKeyedService<ISchemaReader>("postgres"));
    }

    private static void AddRequiredDependencies(IServiceCollection services)
    {
        services.AddLogging();
        services.AddSingleton<IDatabaseStandardsProvider, DatabaseStandardsProvider>();
        services.Configure<MigrationSettings>(s => {
            s.TargetDatabaseType = "postgres";
            s.NamingConvention = "auto";
            s.UseTargetDatabaseStandards = true;
        });
        services.AddSingleton<INamingConverter, SnakeCaseConverter>();
        services.AddSingleton<IDataTypeMapper, UniversalDataTypeMapper>();
        services.AddSingleton<ISqlDialectConverter, SqlDialectConverter>();
        services.AddSingleton<ISqlCollector>(new SqlCollector(false));
    }
}
