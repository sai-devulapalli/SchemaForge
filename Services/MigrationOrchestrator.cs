using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SchemaForge.Configuration;
using SchemaForge.Models;
using SchemaForge.Services.Interfaces;

namespace SchemaForge.Services;

/// <summary>
/// Orchestrates the complete database migration process.
/// Coordinates schema reading, table creation, data migration, and index/constraint creation.
/// Executes migration steps in the correct order to handle dependencies.
/// </summary>
public class MigrationOrchestrator(
    ILogger<MigrationOrchestrator> logger,
    IOptions<MigrationSettings> settings,
    IOptions<MigrationOptions> migrationOptions,
    IServiceProvider serviceProvider,
    IDataMigrator dataMigrator,
    TableDependencySorter dependencySorter,
    ISqlCollector sqlCollector)
{
    private readonly MigrationSettings _settings = settings.Value;
    private readonly MigrationOptions _options = migrationOptions.Value;

    /// <summary>
    /// Executes migration using default options from configuration.
    /// </summary>
    public async Task ExecuteMigrationAsync(CancellationToken cancellationToken = default)
    {
        await ExecuteMigrationAsync(_options, cancellationToken);
    }

    /// <summary>
    /// Executes migration with specified options.
    /// Migration steps: 1) Read schema, 2) Create tables, 3) Migrate data,
    /// 4) Create indexes, 5) Create constraints, 6) Create views.
    /// </summary>
    /// <param name="options">Migration options controlling what to migrate.</param>
    /// <param name="cancellationToken">Token to cancel the migration.</param>
    public async Task ExecuteMigrationAsync(MigrationOptions options, CancellationToken cancellationToken = default)
    {
        try
        {
            LogMigrationPlan(options);

            var sourceDatabaseType = _settings.SourceDatabaseType.ToLowerInvariant();
            var targetDatabaseType = _settings.TargetDatabaseType.ToLowerInvariant();

            var schemaReader = serviceProvider.GetRequiredKeyedService<ISchemaReader>(sourceDatabaseType);
            var schemaWriter = serviceProvider.GetRequiredKeyedService<ISchemaWriter>(targetDatabaseType);

            // Step 1: Read source schema (with table filters for efficiency)
            cancellationToken.ThrowIfCancellationRequested();
            logger.LogInformation("Step 1: Reading source schema...");
            var tables = await schemaReader.ReadSchemaAsync(
                _settings.SourceConnectionString,
                options.IncludeTables.Count > 0 ? options.IncludeTables : null,
                options.ExcludeTables.Count > 0 ? options.ExcludeTables : null);

            // Sort tables by foreign key dependencies (tables with no FKs first, then dependent tables)
            logger.LogInformation("Sorting tables by foreign key dependencies...");
            tables = dependencySorter.SortByDependencies(tables);

            var views = options.MigrateViews
                ? await schemaReader.ReadViewsAsync(_settings.SourceConnectionString)
                : [];

            // Step 2: Create target schema (tables first)
            cancellationToken.ThrowIfCancellationRequested();
            if (options.MigrateSchema)
            {
                logger.LogInformation("Step 2: Creating target tables...");
                await schemaWriter.CreateSchemaAsync(
                    _settings.TargetConnectionString,
                    _settings.TargetSchemaName,
                    tables);
            }
            else
            {
                logger.LogInformation("Step 2: Skipping schema creation (disabled)");
            }

            // Step 3: Migrate data
            cancellationToken.ThrowIfCancellationRequested();
            if (options.MigrateData)
            {
                logger.LogInformation("Step 3: Migrating data...");
                var batchSize = options.DataBatchSize > 0 ? options.DataBatchSize : _settings.BatchSize;
                await dataMigrator.MigrateDataAsync(
                    sourceDatabaseType,
                    targetDatabaseType,
                    _settings.SourceConnectionString,
                    _settings.TargetConnectionString,
                    _settings.TargetSchemaName,
                    tables,
                    batchSize);
            }
            else
            {
                logger.LogInformation("Step 3: Skipping data migration (disabled)");
            }

            // Step 4: Create indexes (after data for better performance)
            cancellationToken.ThrowIfCancellationRequested();
            if (options.MigrateIndexes)
            {
                logger.LogInformation("Step 4: Creating indexes...");
                var allIndexes = tables.SelectMany(t => t.Indexes).ToList();
                await schemaWriter.CreateIndexesAsync(
                    _settings.TargetConnectionString,
                    _settings.TargetSchemaName,
                    allIndexes);
            }
            else
            {
                logger.LogInformation("Step 4: Skipping index creation (disabled)");
            }

            // Step 5: Create constraints (after data)
            cancellationToken.ThrowIfCancellationRequested();
            if (options.MigrateConstraints)
            {
                logger.LogInformation("Step 5: Creating constraints...");
                var allConstraints = tables.SelectMany(t => t.Constraints).ToList();
                await schemaWriter.CreateConstraintsAsync(
                    _settings.TargetConnectionString,
                    _settings.TargetSchemaName,
                    allConstraints);
            }
            else
            {
                logger.LogInformation("Step 5: Skipping constraint creation (disabled)");
            }

            // Step 6: Create views (last, as they may depend on data)
            cancellationToken.ThrowIfCancellationRequested();
            if (options.MigrateViews)
            {
                logger.LogInformation("Step 6: Creating views...");
                await schemaWriter.CreateViewsAsync(
                    _settings.TargetConnectionString,
                    _settings.TargetSchemaName,
                    views);
            }
            else
            {
                logger.LogInformation("Step 6: Skipping view creation (disabled)");
            }

            logger.LogInformation("Migration completed successfully!");
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning("Migration was cancelled");
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Migration failed");
            if (!options.ContinueOnError)
                throw;
        }
    }

    /// <summary>
    /// Executes migration in dry run mode, returning generated SQL without execution.
    /// </summary>
    /// <param name="options">Migration options controlling what to migrate.</param>
    /// <returns>DryRunResult containing generated SQL and summary statistics.</returns>
    public async Task<DryRunResult> ExecuteDryRunAsync(MigrationOptions options, CancellationToken cancellationToken = default)
    {
        options.DryRun.Enabled = true;

        logger.LogInformation("=== DRY RUN MODE ===");
        logger.LogInformation("Generating SQL without executing...");

        await ExecuteMigrationAsync(options, cancellationToken);

        var statements = sqlCollector.GetStatements();
        var script = sqlCollector.GetScript();

        var result = new DryRunResult
        {
            Statements = statements,
            Script = script,
            OutputFilePath = options.DryRun.OutputFilePath,
            Summary = new DryRunSummary
            {
                TableCount = statements.Count(s => s.Category == "Tables"),
                IndexCount = statements.Count(s => s.Category == "Indexes"),
                ConstraintCount = statements.Count(s => s.Category == "Constraints"),
                ViewCount = statements.Count(s => s.Category == "Views"),
                ForeignKeyCount = statements.Count(s => s.Category == "ForeignKeys"),
                TotalStatements = statements.Count(s => s.Category != "Comment")
            }
        };

        // Write to file if path specified
        if (!string.IsNullOrEmpty(options.DryRun.OutputFilePath))
        {
            await File.WriteAllTextAsync(options.DryRun.OutputFilePath, script);
            logger.LogInformation("Dry run SQL written to: {FilePath}", options.DryRun.OutputFilePath);
        }

        logger.LogInformation("=== DRY RUN COMPLETE ===");
        logger.LogInformation("Generated {Count} SQL statements", result.Summary.TotalStatements);

        return result;
    }

    /// <summary>
    /// Logs the migration configuration and options to the console.
    /// </summary>
    private void LogMigrationPlan(MigrationOptions options)
    {
        logger.LogInformation("=== Database Migration Tool ===");
        logger.LogInformation("Source: {SourceType}", _settings.SourceDatabaseType);
        logger.LogInformation("Target: {TargetType} (Schema: {Schema})",
            _settings.TargetDatabaseType, _settings.TargetSchemaName);
        // Fix #14: Log connection targets (database/host only, no credentials)
        LogConnectionTarget("Source", _settings.SourceConnectionString);
        LogConnectionTarget("Target", _settings.TargetConnectionString);
        logger.LogInformation("--- Migration Options ---");
        logger.LogInformation("  Schema:      {Enabled}", options.MigrateSchema ? "Yes" : "No");
        logger.LogInformation("  Data:        {Enabled}", options.MigrateData ? "Yes" : "No");
        logger.LogInformation("  Views:       {Enabled}", options.MigrateViews ? "Yes" : "No");
        logger.LogInformation("  Indexes:     {Enabled}", options.MigrateIndexes ? "Yes" : "No");
        logger.LogInformation("  Constraints: {Enabled}", options.MigrateConstraints ? "Yes" : "No");
        logger.LogInformation("  ForeignKeys: {Enabled}", options.MigrateForeignKeys ? "Yes" : "No");
        if (options.MigrateData)
            logger.LogInformation("  Batch Size:  {BatchSize}", options.DataBatchSize);
        logger.LogInformation("===============================");
    }

    /// <summary>
    /// Filters tables based on include/exclude lists in migration options.
    /// </summary>
    private static List<TableSchema> FilterTables(List<TableSchema> tables, MigrationOptions options)
    {
        var filtered = tables.AsEnumerable();

        // Apply include filter
        if (options.IncludeTables.Count > 0)
        {
            var includeSet = new HashSet<string>(options.IncludeTables, StringComparer.OrdinalIgnoreCase);
            filtered = filtered.Where(t => includeSet.Contains(t.TableName));
        }

        // Apply exclude filter
        if (options.ExcludeTables.Count > 0)
        {
            var excludeSet = new HashSet<string>(options.ExcludeTables, StringComparer.OrdinalIgnoreCase);
            filtered = filtered.Where(t => !excludeSet.Contains(t.TableName));
        }

        return filtered.ToList();
    }

    /// <summary>
    /// Logs the connection target (host/database) without exposing credentials.
    /// Parses common connection string formats to extract safe-to-log info.
    /// </summary>
    private void LogConnectionTarget(string label, string connectionString)
    {
        try
        {
            // Try to extract host/database from connection string without exposing password
            var parts = connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries);
            var safeInfo = new List<string>();
            foreach (var part in parts)
            {
                var kvp = part.Split('=', 2, StringSplitOptions.TrimEntries);
                if (kvp.Length != 2) continue;
                var key = kvp[0].ToLowerInvariant();
                if (key is "server" or "host" or "data source" or "database" or "initial catalog")
                {
                    safeInfo.Add(part.Trim());
                }
            }
            if (safeInfo.Count > 0)
            {
                logger.LogInformation("{Label} connection: {ConnectionInfo}", label, string.Join("; ", safeInfo));
            }
        }
        catch
        {
            // Silently ignore parsing errors for connection string logging
        }
    }
}