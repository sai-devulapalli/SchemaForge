using Microsoft.Extensions.DependencyInjection;
using SchemaForge.Abstractions.Interfaces;
using SchemaForge.Abstractions.Models;

namespace SchemaForge.Providers.Oracle;

/// <summary>
/// Database provider plugin for Oracle.
/// Registers all Oracle-specific services into the DI container.
/// </summary>
public class OracleProvider : IDatabaseProvider
{
    public string ProviderKey => DatabaseTypes.Oracle;

    public void Register(IServiceCollection services)
    {
        services.AddKeyedTransient<ISchemaReader, OracleSchemaReader>(ProviderKey);
        services.AddKeyedTransient<ISchemaWriter, OracleSchemaWriter>(ProviderKey);
        services.AddKeyedTransient<Abstractions.Interfaces.IDataReader, OracleDataReader>(ProviderKey);
        services.AddKeyedTransient<IDataWriter, OracleDataWriter>(ProviderKey);
    }
}