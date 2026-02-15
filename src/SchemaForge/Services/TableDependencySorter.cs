using SchemaForge.Abstractions.Models;
using Microsoft.Extensions.Logging;

namespace SchemaForge.Services;

/// <summary>
/// Sorts tables based on foreign key dependencies using topological sort.
/// Tables without dependencies (primary key tables) come first,
/// followed by tables that depend on them (foreign key tables).
/// </summary>
public class TableDependencySorter(ILogger<TableDependencySorter> logger)
{
    /// <summary>
    /// Sorts tables in dependency order using topological sort (Kahn's algorithm).
    /// Tables with no foreign key dependencies are placed first,
    /// followed by tables that reference them.
    /// </summary>
    /// <param name="tables">The list of tables to sort</param>
    /// <returns>Tables sorted in dependency order</returns>
    public List<TableSchema> SortByDependencies(List<TableSchema> tables)
    {
        logger.LogInformation("Sorting {Count} tables by foreign key dependencies...", tables.Count);

        // Build lookup for quick table access by name
        var tableByName = tables.ToDictionary(
            t => GetTableKey(t.SchemaName, t.TableName),
            t => t,
            StringComparer.OrdinalIgnoreCase);

        // Build dependency graph: table -> tables it depends on (referenced tables)
        var dependencies = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        // Build reverse dependency graph: table -> tables that depend on it
        var dependents = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        // Initialize all tables in the graphs
        foreach (var table in tables)
        {
            var key = GetTableKey(table.SchemaName, table.TableName);
            dependencies[key] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            dependents[key] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        // Populate dependencies from foreign keys
        foreach (var table in tables)
        {
            var tableKey = GetTableKey(table.SchemaName, table.TableName);

            foreach (var fk in table.ForeignKeys)
            {
                var referencedKey = GetTableKey(fk.ReferencedSchema, fk.ReferencedTable);

                // Skip self-referencing tables (they don't create ordering requirements)
                if (tableKey.Equals(referencedKey, StringComparison.OrdinalIgnoreCase))
                {
                    logger.LogDebug("Skipping self-reference: {Table}", table.TableName);
                    continue;
                }

                // Only add dependency if the referenced table is in our list
                if (tableByName.ContainsKey(referencedKey))
                {
                    dependencies[tableKey].Add(referencedKey);
                    dependents[referencedKey].Add(tableKey);
                }
                else
                {
                    logger.LogDebug("Table {Table} references {Referenced} which is not in migration set",
                        table.TableName, fk.ReferencedTable);
                }
            }
        }

        // Kahn's algorithm for topological sort
        var sorted = new List<TableSchema>();
        var queue = new Queue<string>();

        // Find all tables with no dependencies (in-degree = 0)
        foreach (var kvp in dependencies)
        {
            if (kvp.Value.Count == 0)
            {
                queue.Enqueue(kvp.Key);
            }
        }

        logger.LogDebug("Starting sort with {Count} independent tables", queue.Count);

        while (queue.Count > 0)
        {
            var currentKey = queue.Dequeue();
            sorted.Add(tableByName[currentKey]);

            // Remove this table from all dependents
            foreach (var dependent in dependents[currentKey])
            {
                dependencies[dependent].Remove(currentKey);

                // If dependent now has no more dependencies, add to queue
                if (dependencies[dependent].Count == 0)
                {
                    queue.Enqueue(dependent);
                }
            }
        }

        // Check for circular dependencies
        if (sorted.Count != tables.Count)
        {
            var unsorted = tables
                .Where(t => !sorted.Contains(t))
                .Select(t => t.TableName)
                .ToList();

            logger.LogWarning(
                "Circular dependency detected among tables: {Tables}. These will be appended at the end.",
                string.Join(", ", unsorted));

            // Append remaining tables (they have circular dependencies)
            // They will be handled by the constraint disable/enable mechanism
            foreach (var table in tables.Where(t => !sorted.Contains(t)))
            {
                sorted.Add(table);
            }
        }

        LogSortResult(sorted);
        return sorted;
    }

    private static string GetTableKey(string schemaName, string tableName)
    {
        return $"{schemaName}.{tableName}";
    }

    private void LogSortResult(List<TableSchema> sorted)
    {
        logger.LogInformation("Table processing order ({Count} tables):", sorted.Count);
        for (int i = 0; i < sorted.Count; i++)
        {
            var table = sorted[i];
            var fkCount = table.ForeignKeys.Count;
            var fkInfo = fkCount > 0 ? $" (FK refs: {fkCount})" : " (no FKs)";
            logger.LogInformation("  {Order}. {Schema}.{Table}{FkInfo}",
                i + 1, table.SchemaName, table.TableName, fkInfo);
        }
    }
}