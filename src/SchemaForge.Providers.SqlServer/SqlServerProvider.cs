using Microsoft.Extensions.DependencyInjection;
using SchemaForge.Abstractions.Interfaces;
using SchemaForge.Abstractions.Models;

namespace SchemaForge.Providers.SqlServer;

/// <summary>
/// Database provider plugin for SQL Server.
/// Registers all SQL Server-specific services into the DI container.
/// </summary>
public class SqlServerProvider : IDatabaseProvider
{
    public string ProviderKey => DatabaseTypes.SqlServer;

    public void Register(IServiceCollection services)
    {
        services.AddKeyedTransient<ISchemaReader, SqlServerSchemaReader>(ProviderKey);
        services.AddKeyedTransient<ISchemaWriter, SqlServerSchemaWriter>(ProviderKey);
        services.AddKeyedTransient<Abstractions.Interfaces.IDataReader, SqlServerDataReader>(ProviderKey);
        services.AddKeyedTransient<IDataWriter, SqlServerDataWriter>(ProviderKey);
    }
}