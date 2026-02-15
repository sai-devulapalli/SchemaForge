using Microsoft.Extensions.DependencyInjection;
using SchemaForge.Abstractions.Interfaces;

namespace SchemaForge.Services;

/// <summary>
/// Discovers and loads database provider plugins from loaded assemblies.
/// Scans all loaded assemblies for types implementing IDatabaseProvider and registers them.
/// </summary>
public class AssemblyPluginLoader : IPluginLoader
{
    /// <summary>
    /// Discovers all IDatabaseProvider implementations in loaded assemblies
    /// and calls their Register method to add services to the DI container.
    /// </summary>
    public void LoadProviders(IServiceCollection services)
    {
        var providerTypes = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(a =>
            {
                try { return a.GetTypes(); }
                catch { return []; }
            })
            .Where(t => typeof(IDatabaseProvider).IsAssignableFrom(t)
                     && !t.IsAbstract
                     && !t.IsInterface);

        foreach (var type in providerTypes)
        {
            var provider = (IDatabaseProvider)Activator.CreateInstance(type)!;
            provider.Register(services);
        }
    }
}