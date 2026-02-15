using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SchemaForge.Abstractions.Configuration;
using SchemaForge.Abstractions.Interfaces;
using SchemaForge.Abstractions.Models;
using SchemaForge.Services;

namespace SchemaForge.Builder;

/// <summary>
/// Fluent builder for configuring and executing database migrations.
/// Provides a clean, chainable API for setting up migrations.
/// </summary>
public class MigrationBuilder
{
    private readonly MigrationSettings _settings = new();
    private readonly MigrationOptions _options = new();
    private Action<ILoggingBuilder>? _loggingConfig;
    private LogLevel _minLogLevel = LogLevel.Information;

    #region Source Database Configuration

    /// <summary>
    /// Configure SQL Server as the source database.
    /// </summary>
    public MigrationBuilder FromSqlServer(string connectionString)
    {
        _settings.SourceDatabaseType = DatabaseTypes.SqlServer;
        _settings.SourceConnectionString = connectionString;
        return this;
    }

    /// <summary>
    /// Configure PostgreSQL as the source database.
    /// </summary>
    public MigrationBuilder FromPostgres(string connectionString)
    {
        _settings.SourceDatabaseType = DatabaseTypes.PostgreSql;
        _settings.SourceConnectionString = connectionString;
        return this;
    }

    /// <summary>
    /// Configure MySQL as the source database.
    /// </summary>
    public MigrationBuilder FromMySql(string connectionString)
    {
        _settings.SourceDatabaseType = DatabaseTypes.MySql;
        _settings.SourceConnectionString = connectionString;
        return this;
    }

    /// <summary>
    /// Configure Oracle as the source database.
    /// </summary>
    public MigrationBuilder FromOracle(string connectionString)
    {
        _settings.SourceDatabaseType = DatabaseTypes.Oracle;
        _settings.SourceConnectionString = connectionString;
        return this;
    }

    /// <summary>
    /// Configure source database with explicit type.
    /// </summary>
    public MigrationBuilder From(string databaseType, string connectionString)
    {
        _settings.SourceDatabaseType = databaseType.ToLowerInvariant();
        _settings.SourceConnectionString = connectionString;
        return this;
    }

    #endregion

    #region Target Database Configuration

    /// <summary>
    /// Configure SQL Server as the target database.
    /// </summary>
    public MigrationBuilder ToSqlServer(string connectionString, string schemaName = "dbo")
    {
        _settings.TargetDatabaseType = DatabaseTypes.SqlServer;
        _settings.TargetConnectionString = connectionString;
        _settings.TargetSchemaName = schemaName;
        return this;
    }

    /// <summary>
    /// Configure PostgreSQL as the target database.
    /// </summary>
    public MigrationBuilder ToPostgres(string connectionString, string schemaName = "public")
    {
        _settings.TargetDatabaseType = DatabaseTypes.PostgreSql;
        _settings.TargetConnectionString = connectionString;
        _settings.TargetSchemaName = schemaName;
        return this;
    }

    /// <summary>
    /// Configure MySQL as the target database.
    /// </summary>
    public MigrationBuilder ToMySql(string connectionString, string schemaName)
    {
        _settings.TargetDatabaseType = DatabaseTypes.MySql;
        _settings.TargetConnectionString = connectionString;
        _settings.TargetSchemaName = schemaName;
        return this;
    }

    /// <summary>
    /// Configure Oracle as the target database.
    /// </summary>
    public MigrationBuilder ToOracle(string connectionString, string schemaName)
    {
        _settings.TargetDatabaseType = DatabaseTypes.Oracle;
        _settings.TargetConnectionString = connectionString;
        _settings.TargetSchemaName = schemaName;
        return this;
    }

    /// <summary>
    /// Configure target database with explicit type.
    /// </summary>
    public MigrationBuilder To(string databaseType, string connectionString, string schemaName)
    {
        _settings.TargetDatabaseType = databaseType.ToLowerInvariant();
        _settings.TargetConnectionString = connectionString;
        _settings.TargetSchemaName = schemaName;
        return this;
    }

    #endregion

    #region Migration Options

    public MigrationBuilder MigrateAll()
    {
        _options.MigrateSchema = true;
        _options.MigrateData = true;
        _options.MigrateViews = true;
        _options.MigrateIndexes = true;
        _options.MigrateConstraints = true;
        _options.MigrateForeignKeys = true;
        return this;
    }

    public MigrationBuilder MigrateSchemaOnly()
    {
        _options.MigrateSchema = true;
        _options.MigrateData = false;
        _options.MigrateViews = true;
        _options.MigrateIndexes = true;
        _options.MigrateConstraints = true;
        _options.MigrateForeignKeys = true;
        return this;
    }

    public MigrationBuilder MigrateDataOnly()
    {
        _options.MigrateSchema = false;
        _options.MigrateData = true;
        _options.MigrateViews = false;
        _options.MigrateIndexes = false;
        _options.MigrateConstraints = false;
        _options.MigrateForeignKeys = false;
        return this;
    }

    public MigrationBuilder WithSchema() { _options.MigrateSchema = true; return this; }
    public MigrationBuilder WithoutSchema() { _options.MigrateSchema = false; return this; }
    public MigrationBuilder WithData() { _options.MigrateData = true; return this; }
    public MigrationBuilder WithoutData() { _options.MigrateData = false; return this; }
    public MigrationBuilder WithViews() { _options.MigrateViews = true; return this; }
    public MigrationBuilder WithoutViews() { _options.MigrateViews = false; return this; }
    public MigrationBuilder WithIndexes() { _options.MigrateIndexes = true; return this; }
    public MigrationBuilder WithoutIndexes() { _options.MigrateIndexes = false; return this; }
    public MigrationBuilder WithConstraints() { _options.MigrateConstraints = true; return this; }
    public MigrationBuilder WithoutConstraints() { _options.MigrateConstraints = false; return this; }
    public MigrationBuilder WithForeignKeys() { _options.MigrateForeignKeys = true; return this; }
    public MigrationBuilder WithoutForeignKeys() { _options.MigrateForeignKeys = false; return this; }

    #endregion

    #region Table Filtering

    public MigrationBuilder IncludeTables(params string[] tableNames)
    {
        _options.IncludeTables = tableNames.ToList();
        return this;
    }

    public MigrationBuilder IncludeTables(IEnumerable<string> tableNames)
    {
        _options.IncludeTables = tableNames.ToList();
        return this;
    }

    public MigrationBuilder ExcludeTables(params string[] tableNames)
    {
        _options.ExcludeTables = tableNames.ToList();
        return this;
    }

    public MigrationBuilder ExcludeTables(IEnumerable<string> tableNames)
    {
        _options.ExcludeTables = tableNames.ToList();
        return this;
    }

    #endregion

    #region Performance & Behavior

    public MigrationBuilder WithBatchSize(int batchSize)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(batchSize, nameof(batchSize));
        _options.DataBatchSize = batchSize;
        _settings.BatchSize = batchSize;
        return this;
    }

    public MigrationBuilder ContinueOnError() { _options.ContinueOnError = true; return this; }
    public MigrationBuilder StopOnError() { _options.ContinueOnError = false; return this; }

    #endregion

    #region Dry Run

    public MigrationBuilder DryRun() { _options.DryRun.Enabled = true; return this; }

    public MigrationBuilder DryRun(string outputPath)
    {
        _options.DryRun.Enabled = true;
        _options.DryRun.OutputFilePath = outputPath;
        return this;
    }

    public MigrationBuilder WithDataSamples(int sampleCount = 5)
    {
        _options.DryRun.IncludeDataSamples = true;
        _options.DryRun.SampleRowCount = sampleCount;
        return this;
    }

    public MigrationBuilder WithoutDataSamples() { _options.DryRun.IncludeDataSamples = false; return this; }

    #endregion

    #region Naming Convention

    public MigrationBuilder WithNamingConvention(NamingConvention convention)
    {
        _settings.NamingConvention = convention switch
        {
            NamingConvention.SnakeCase => "snake_case",
            NamingConvention.PascalCase => "PascalCase",
            NamingConvention.Lowercase => "lowercase",
            NamingConvention.Uppercase => "UPPERCASE",
            NamingConvention.Preserve => "preserve",
            _ => "auto"
        };
        return this;
    }

    public MigrationBuilder WithAutoNaming()
    {
        _settings.NamingConvention = "auto";
        _settings.UseTargetDatabaseStandards = true;
        return this;
    }

    public MigrationBuilder PreserveNames()
    {
        _settings.NamingConvention = "preserve";
        _settings.PreserveSourceCase = true;
        return this;
    }

    public MigrationBuilder WithMaxIdentifierLength(int maxLength)
    {
        _settings.MaxIdentifierLength = maxLength;
        return this;
    }

    #endregion

    #region Logging

    public MigrationBuilder ConfigureLogging(Action<ILoggingBuilder> configure)
    {
        _loggingConfig = configure;
        return this;
    }

    public MigrationBuilder WithLogLevel(LogLevel level) { _minLogLevel = level; return this; }
    public MigrationBuilder Verbose() { _minLogLevel = LogLevel.Debug; return this; }
    public MigrationBuilder Quiet() { _minLogLevel = LogLevel.Warning; return this; }

    #endregion

    #region Execution

    /// <summary>
    /// Build the migration configuration and return the orchestrator.
    /// </summary>
    public MigrationOrchestrator Build()
    {
        ValidateConfiguration();
        var serviceProvider = BuildServiceProvider();
        return serviceProvider.GetRequiredService<MigrationOrchestrator>();
    }

    public async Task<DryRunResult?> ExecuteAsync()
    {
        var orchestrator = Build();
        if (_options.DryRun.Enabled)
            return await orchestrator.ExecuteDryRunAsync(_options);
        await orchestrator.ExecuteMigrationAsync(_options);
        return null;
    }

    public async Task<DryRunResult?> ExecuteAsync(CancellationToken cancellationToken)
    {
        var orchestrator = Build();
        if (_options.DryRun.Enabled)
            return await orchestrator.ExecuteDryRunAsync(_options, cancellationToken);
        await orchestrator.ExecuteMigrationAsync(_options, cancellationToken);
        return null;
    }

    public async Task<DryRunResult> ExecuteDryRunAsync(CancellationToken cancellationToken = default)
    {
        _options.DryRun.Enabled = true;
        var orchestrator = Build();
        return await orchestrator.ExecuteDryRunAsync(_options, cancellationToken);
    }

    public MigrationSettings GetSettings() => _settings;
    public MigrationOptions GetOptions() => _options;

    #endregion

    #region Private Methods

    private void ValidateConfiguration()
    {
        var errors = new List<string>();

        if (string.IsNullOrEmpty(_settings.SourceConnectionString))
            errors.Add("Source connection string is required. Use From*() methods.");

        if (string.IsNullOrEmpty(_settings.TargetConnectionString))
            errors.Add("Target connection string is required. Use To*() methods.");

        if (string.IsNullOrEmpty(_settings.SourceDatabaseType))
            errors.Add("Source database type is required.");

        if (string.IsNullOrEmpty(_settings.TargetDatabaseType))
            errors.Add("Target database type is required.");

        if (errors.Count > 0)
            throw new InvalidOperationException($"Migration configuration errors:\n- {string.Join("\n- ", errors)}");
    }

    private ServiceProvider BuildServiceProvider()
    {
        var services = new ServiceCollection();

        // Configure logging
        services.AddLogging(builder =>
        {
            if (_loggingConfig != null)
                _loggingConfig(builder);
            else
            {
                builder.AddConsole();
                builder.SetMinimumLevel(_minLogLevel);
            }
        });

        // Configure settings
        services.Configure<MigrationSettings>(opts =>
        {
            opts.SourceDatabaseType = _settings.SourceDatabaseType;
            opts.TargetDatabaseType = _settings.TargetDatabaseType;
            opts.SourceConnectionString = _settings.SourceConnectionString;
            opts.TargetConnectionString = _settings.TargetConnectionString;
            opts.TargetSchemaName = _settings.TargetSchemaName;
            opts.BatchSize = _settings.BatchSize;
            opts.NamingConvention = _settings.NamingConvention;
            opts.UseTargetDatabaseStandards = _settings.UseTargetDatabaseStandards;
            opts.PreserveSourceCase = _settings.PreserveSourceCase;
            opts.MaxIdentifierLength = _settings.MaxIdentifierLength;
        });

        // Configure migration options
        services.Configure<MigrationOptions>(opts =>
        {
            opts.MigrateSchema = _options.MigrateSchema;
            opts.MigrateData = _options.MigrateData;
            opts.MigrateViews = _options.MigrateViews;
            opts.MigrateIndexes = _options.MigrateIndexes;
            opts.MigrateConstraints = _options.MigrateConstraints;
            opts.MigrateForeignKeys = _options.MigrateForeignKeys;
            opts.DataBatchSize = _options.DataBatchSize;
            opts.ContinueOnError = _options.ContinueOnError;
            opts.IncludeTables = _options.IncludeTables;
            opts.ExcludeTables = _options.ExcludeTables;
        });

        // Load database providers via plugin discovery
        var pluginLoader = new AssemblyPluginLoader();
        pluginLoader.LoadProviders(services);

        // Register SQL collector for dry run support
        services.AddSingleton<ISqlCollector>(sp =>
            new SqlCollector(_options.DryRun.Enabled, _options.DryRun.IncludeComments));

        // Register core services
        services.AddSingleton<IDataMigrator, BulkDataMigrator>();
        services.AddSingleton<INamingConverter, SnakeCaseConverter>();
        services.AddSingleton<ISqlDialectConverter, SqlDialectConverter>();
        services.AddSingleton<IDataTypeMapper, UniversalDataTypeMapper>();
        services.AddSingleton<IDatabaseStandardsProvider, DatabaseStandardsProvider>();
        services.AddSingleton<TableDependencySorter>();
        services.AddSingleton<MigrationOrchestrator>();

        return services.BuildServiceProvider();
    }

    #endregion
}