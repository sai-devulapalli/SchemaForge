using Microsoft.Extensions.DependencyInjection;

namespace SchemaForge.Abstractions.Interfaces;

/// <summary>
/// Plugin contract for database providers.
/// Each database provider implements this interface to self-register its services.
/// This is the primary extension point for adding new database support to SchemaForge.
/// </summary>
public interface IDatabaseProvider
{
    /// <summary>
    /// The unique provider key used to identify this database type (e.g., "sqlserver", "postgres").
    /// This key is used for keyed DI resolution at runtime.
    /// </summary>
    string ProviderKey { get; }

    /// <summary>
    /// Registers this provider's services into the DI container.
    /// Implementations should register keyed services for ISchemaReader, ISchemaWriter,
    /// IDataReader, and IDataWriter using the ProviderKey.
    /// </summary>
    /// <param name="services">The service collection to register into.</param>
    void Register(IServiceCollection services);
}