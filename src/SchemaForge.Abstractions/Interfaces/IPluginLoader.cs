using Microsoft.Extensions.DependencyInjection;

namespace SchemaForge.Abstractions.Interfaces;

/// <summary>
/// Discovers and loads database provider plugins into the DI container.
/// </summary>
public interface IPluginLoader
{
    /// <summary>
    /// Discovers all available database providers and registers them into the service collection.
    /// </summary>
    /// <param name="services">The service collection to register providers into.</param>
    void LoadProviders(IServiceCollection services);
}