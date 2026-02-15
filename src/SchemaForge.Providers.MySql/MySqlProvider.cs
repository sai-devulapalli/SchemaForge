using Microsoft.Extensions.DependencyInjection;
using SchemaForge.Abstractions.Interfaces;
using SchemaForge.Abstractions.Models;

namespace SchemaForge.Providers.MySql;

/// <summary>
/// Database provider plugin for MySQL.
/// Registers all MySQL-specific services into the DI container.
/// </summary>
public class MySqlProvider : IDatabaseProvider
{
    public string ProviderKey => DatabaseTypes.MySql;

    public void Register(IServiceCollection services)
    {
        services.AddKeyedTransient<ISchemaReader, MySqlSchemaReader>(ProviderKey);
        services.AddKeyedTransient<ISchemaWriter, MySqlSchemaWriter>(ProviderKey);
        services.AddKeyedTransient<Abstractions.Interfaces.IDataReader, MySqlDataReader>(ProviderKey);
        services.AddKeyedTransient<IDataWriter, MySqlDataWriter>(ProviderKey);
    }
}