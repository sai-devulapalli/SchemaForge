using Microsoft.Extensions.DependencyInjection;
using SchemaForge.Abstractions.Interfaces;
using SchemaForge.Abstractions.Models;

namespace SchemaForge.Providers.Postgres;

/// <summary>
/// Database provider plugin for PostgreSQL.
/// Registers all PostgreSQL-specific services into the DI container.
/// </summary>
public class PostgresProvider : IDatabaseProvider
{
    public string ProviderKey => DatabaseTypes.PostgreSql;

    public void Register(IServiceCollection services)
    {
        services.AddKeyedTransient<ISchemaReader, PostgresSchemaReader>(ProviderKey);
        services.AddKeyedTransient<ISchemaWriter, PostgresSchemaWriter>(ProviderKey);
        services.AddKeyedTransient<Abstractions.Interfaces.IDataReader, PostgresDataReader>(ProviderKey);
        services.AddKeyedTransient<IDataWriter, PostgresDataWriter>(ProviderKey);
    }
}