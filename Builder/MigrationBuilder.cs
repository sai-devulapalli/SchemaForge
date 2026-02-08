using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SchemaForge.Configuration;
using SchemaForge.Models;
using SchemaForge.Services;
using SchemaForge.Services.Interfaces;
using SchemaForge.Services.DataReader;
using SchemaForge.Services.DataWriter;
using SchemaForge.Services.SchemaReader;
using SchemaForge.Services.SchemaWriter;

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
    /// <param name="connectionString">SQL Server connection string.</param>
    public MigrationBuilder FromSqlServer(string connectionString)
    {
        _settings.SourceDatabaseType = DatabaseTypes.SqlServer;
        _settings.SourceConnectionString = connectionString;
        return this;
    }

    /// <summary>
    /// Configure PostgreSQL as the source database.
    /// </summary>
    /// <param name="connectionString">PostgreSQL connection string.</param>
    public MigrationBuilder FromPostgres(string connectionString)
    {
        _settings.SourceDatabaseType = DatabaseTypes.PostgreSql;
        _settings.SourceConnectionString = connectionString;
        return this;
    }

    /// <summary>
    /// Configure MySQL as the source database.
    /// </summary>
    /// <param name="connectionString">MySQL connection string.</param>
    public MigrationBuilder FromMySql(string connectionString)
    {
        _settings.SourceDatabaseType = DatabaseTypes.MySql;
        _settings.SourceConnectionString = connectionString;
        return this;
    }

    /// <summary>
    /// Configure Oracle as the source database.
    /// </summary>
    /// <param name="connectionString">Oracle connection string.</param>
    public MigrationBuilder FromOracle(string connectionString)
    {
        _settings.SourceDatabaseType = DatabaseTypes.Oracle;
        _settings.SourceConnectionString = connectionString;
        return this;
    }

    /// <summary>
    /// Configure source database with explicit type.
    /// </summary>
    /// <param name="databaseType">Database type: sqlserver, postgres, mysql, oracle.</param>
    /// <param name="connectionString">Connection string.</param>
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
    /// <param name="connectionString">SQL Server connection string.</param>
    /// <param name="schemaName">Target schema name (default: dbo).</param>
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
    /// <param name="connectionString">PostgreSQL connection string.</param>
    /// <param name="schemaName">Target schema name (default: public).</param>
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
    /// <param name="connectionString">MySQL connection string.</param>
    /// <param name="schemaName">Target database/schema name.</param>
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
    /// <param name="connectionString">Oracle connection string.</param>
    /// <param name="schemaName">Target schema name.</param>
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
    /// <param name="databaseType">Database type: sqlserver, postgres, mysql, oracle.</param>
    /// <param name="connectionString">Connection string.</param>
    /// <param name="schemaName">Target schema name.</param>
    public MigrationBuilder To(string databaseType, string connectionString, string schemaName)
    {
        _settings.TargetDatabaseType = databaseType.ToLowerInvariant();
        _settings.TargetConnectionString = connectionString;
        _settings.TargetSchemaName = schemaName;
        return this;
    }

    #endregion

    #region Migration Options

    /// <summary>
    /// Migrate everything: schema, data, views, indexes, constraints, foreign keys.
    /// </summary>
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

    /// <summary>
    /// Migrate only schema (tables, columns, primary keys) without data.
    /// </summary>
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

    /// <summary>
    /// Migrate only data (assumes schema already exists).
    /// </summary>
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

    /// <summary>
    /// Enable schema migration (tables, columns, primary keys).
    /// </summary>
    public MigrationBuilder WithSchema()
    {
        _options.MigrateSchema = true;
        return this;
    }

    /// <summary>
    /// Disable schema migration.
    /// </summary>
    public MigrationBuilder WithoutSchema()
    {
        _options.MigrateSchema = false;
        return this;
    }

    /// <summary>
    /// Enable data migration.
    /// </summary>
    public MigrationBuilder WithData()
    {
        _options.MigrateData = true;
        return this;
    }

    /// <summary>
    /// Disable data migration.
    /// </summary>
    public MigrationBuilder WithoutData()
    {
        _options.MigrateData = false;
        return this;
    }

    /// <summary>
    /// Enable view migration.
    /// </summary>
    public MigrationBuilder WithViews()
    {
        _options.MigrateViews = true;
        return this;
    }

    /// <summary>
    /// Disable view migration.
    /// </summary>
    public MigrationBuilder WithoutViews()
    {
        _options.MigrateViews = false;
        return this;
    }

    /// <summary>
    /// Enable index migration.
    /// </summary>
    public MigrationBuilder WithIndexes()
    {
        _options.MigrateIndexes = true;
        return this;
    }

    /// <summary>
    /// Disable index migration.
    /// </summary>
    public MigrationBuilder WithoutIndexes()
    {
        _options.MigrateIndexes = false;
        return this;
    }

    /// <summary>
    /// Enable constraint migration (CHECK, UNIQUE, DEFAULT).
    /// </summary>
    public MigrationBuilder WithConstraints()
    {
        _options.MigrateConstraints = true;
        return this;
    }

    /// <summary>
    /// Disable constraint migration.
    /// </summary>
    public MigrationBuilder WithoutConstraints()
    {
        _options.MigrateConstraints = false;
        return this;
    }

    /// <summary>
    /// Enable foreign key migration.
    /// </summary>
    public MigrationBuilder WithForeignKeys()
    {
        _options.MigrateForeignKeys = true;
        return this;
    }

    /// <summary>
    /// Disable foreign key migration.
    /// </summary>
    public MigrationBuilder WithoutForeignKeys()
    {
        _options.MigrateForeignKeys = false;
        return this;
    }

    #endregion

    #region Table Filtering

    /// <summary>
    /// Only migrate the specified tables.
    /// </summary>
    /// <param name="tableNames">Table names to include.</param>
    public MigrationBuilder IncludeTables(params string[] tableNames)
    {
        _options.IncludeTables = tableNames.ToList();
        return this;
    }

    /// <summary>
    /// Only migrate the specified tables.
    /// </summary>
    /// <param name="tableNames">Table names to include.</param>
    public MigrationBuilder IncludeTables(IEnumerable<string> tableNames)
    {
        _options.IncludeTables = tableNames.ToList();
        return this;
    }

    /// <summary>
    /// Exclude the specified tables from migration.
    /// </summary>
    /// <param name="tableNames">Table names to exclude.</param>
    public MigrationBuilder ExcludeTables(params string[] tableNames)
    {
        _options.ExcludeTables = tableNames.ToList();
        return this;
    }

    /// <summary>
    /// Exclude the specified tables from migration.
    /// </summary>
    /// <param name="tableNames">Table names to exclude.</param>
    public MigrationBuilder ExcludeTables(IEnumerable<string> tableNames)
    {
        _options.ExcludeTables = tableNames.ToList();
        return this;
    }

    #endregion

    #region Performance & Behavior

    /// <summary>
    /// Set the batch size for data migration.
    /// </summary>
    /// <param name="batchSize">Number of rows per batch (default: 1000).</param>
    public MigrationBuilder WithBatchSize(int batchSize)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(batchSize, nameof(batchSize));
        _options.DataBatchSize = batchSize;
        _settings.BatchSize = batchSize;
        return this;
    }

    /// <summary>
    /// Continue migration even if some objects fail.
    /// </summary>
    public MigrationBuilder ContinueOnError()
    {
        _options.ContinueOnError = true;
        return this;
    }

    /// <summary>
    /// Stop migration on first error.
    /// </summary>
    public MigrationBuilder StopOnError()
    {
        _options.ContinueOnError = false;
        return this;
    }

    #endregion

    #region Dry Run

    /// <summary>
    /// Enable dry run mode - generates SQL without executing.
    /// </summary>
    public MigrationBuilder DryRun()
    {
        _options.DryRun.Enabled = true;
        return this;
    }

    /// <summary>
    /// Enable dry run mode with output to file.
    /// </summary>
    /// <param name="outputPath">Path to write generated SQL script.</param>
    public MigrationBuilder DryRun(string outputPath)
    {
        _options.DryRun.Enabled = true;
        _options.DryRun.OutputFilePath = outputPath;
        return this;
    }

    /// <summary>
    /// Include sample INSERT statements for data preview.
    /// </summary>
    /// <param name="sampleCount">Number of sample rows per table (default: 5).</param>
    public MigrationBuilder WithDataSamples(int sampleCount = 5)
    {
        _options.DryRun.IncludeDataSamples = true;
        _options.DryRun.SampleRowCount = sampleCount;
        return this;
    }

    /// <summary>
    /// Exclude data samples from dry run output.
    /// </summary>
    public MigrationBuilder WithoutDataSamples()
    {
        _options.DryRun.IncludeDataSamples = false;
        return this;
    }

    #endregion

    #region Naming Convention

    /// <summary>
    /// Set the naming convention for identifiers.
    /// </summary>
    /// <param name="convention">Naming convention to use.</param>
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

    /// <summary>
    /// Use automatic naming convention based on target database standards.
    /// </summary>
    public MigrationBuilder WithAutoNaming()
    {
        _settings.NamingConvention = "auto";
        _settings.UseTargetDatabaseStandards = true;
        return this;
    }

    /// <summary>
    /// Preserve original identifier names from source database.
    /// </summary>
    public MigrationBuilder PreserveNames()
    {
        _settings.NamingConvention = "preserve";
        _settings.PreserveSourceCase = true;
        return this;
    }

    /// <summary>
    /// Set the maximum identifier length.
    /// </summary>
    /// <param name="maxLength">Maximum length (0 = use target database default).</param>
    public MigrationBuilder WithMaxIdentifierLength(int maxLength)
    {
        _settings.MaxIdentifierLength = maxLength;
        return this;
    }

    #endregion

    #region Logging

    /// <summary>
    /// Configure logging for the migration.
    /// </summary>
    /// <param name="configure">Logging configuration action.</param>
    public MigrationBuilder ConfigureLogging(Action<ILoggingBuilder> configure)
    {
        _loggingConfig = configure;
        return this;
    }

    /// <summary>
    /// Set minimum log level.
    /// </summary>
    /// <param name="level">Minimum log level.</param>
    public MigrationBuilder WithLogLevel(LogLevel level)
    {
        _minLogLevel = level;
        return this;
    }

    /// <summary>
    /// Enable verbose logging (Debug level).
    /// </summary>
    public MigrationBuilder Verbose()
    {
        _minLogLevel = LogLevel.Debug;
        return this;
    }

    /// <summary>
    /// Enable quiet mode (Warning level and above).
    /// </summary>
    public MigrationBuilder Quiet()
    {
        _minLogLevel = LogLevel.Warning;
        return this;
    }

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

    /// <summary>
    /// Execute the migration asynchronously.
    /// If DryRun() was called, generates SQL without executing and returns the result.
    /// </summary>
    public async Task<DryRunResult?> ExecuteAsync()
    {
        var orchestrator = Build();

        if (_options.DryRun.Enabled)
        {
            return await orchestrator.ExecuteDryRunAsync(_options);
        }

        await orchestrator.ExecuteMigrationAsync(_options);
        return null;
    }

    /// <summary>
    /// Execute the migration asynchronously with cancellation support.
    /// If DryRun() was called, generates SQL without executing.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<DryRunResult?> ExecuteAsync(CancellationToken cancellationToken)
    {
        var orchestrator = Build();

        if (_options.DryRun.Enabled)
        {
            return await orchestrator.ExecuteDryRunAsync(_options, cancellationToken);
        }

        await orchestrator.ExecuteMigrationAsync(_options, cancellationToken);
        return null;
    }

    /// <summary>
    /// Execute dry run and return generated SQL without executing.
    /// Explicitly enables dry run mode.
    /// </summary>
    public async Task<DryRunResult> ExecuteDryRunAsync(CancellationToken cancellationToken = default)
    {
        _options.DryRun.Enabled = true;
        var orchestrator = Build();
        return await orchestrator.ExecuteDryRunAsync(_options, cancellationToken);
    }

    /// <summary>
    /// Get the current configuration settings.
    /// </summary>
    public MigrationSettings GetSettings() => _settings;

    /// <summary>
    /// Get the current migration options.
    /// </summary>
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

        var validTypes = new[] { DatabaseTypes.SqlServer, DatabaseTypes.PostgreSql, DatabaseTypes.MySql, DatabaseTypes.Oracle };

        if (!validTypes.Contains(_settings.SourceDatabaseType?.ToLowerInvariant()))
            errors.Add($"Invalid source database type: {_settings.SourceDatabaseType}. Valid types: {string.Join(", ", validTypes)}");

        if (!validTypes.Contains(_settings.TargetDatabaseType?.ToLowerInvariant()))
            errors.Add($"Invalid target database type: {_settings.TargetDatabaseType}. Valid types: {string.Join(", ", validTypes)}");

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
            {
                _loggingConfig(builder);
            }
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

        // Register schema readers
        services.AddKeyedSingleton<ISchemaReader, SqlServerSchemaReader>(DatabaseTypes.SqlServer);
        services.AddKeyedSingleton<ISchemaReader, MySqlSchemaReader>(DatabaseTypes.MySql);
        services.AddKeyedSingleton<ISchemaReader, OracleSchemaReader>(DatabaseTypes.Oracle);
        services.AddKeyedSingleton<ISchemaReader, PostgresSchemaReader>(DatabaseTypes.PostgreSql);

        // Register schema writers
        services.AddKeyedSingleton<ISchemaWriter, PostgresSchemaWriter>(DatabaseTypes.PostgreSql);
        services.AddKeyedSingleton<ISchemaWriter, MySqlSchemaWriter>(DatabaseTypes.MySql);
        services.AddKeyedSingleton<ISchemaWriter, OracleSchemaWriter>(DatabaseTypes.Oracle);
        services.AddKeyedSingleton<ISchemaWriter, SqlServerSchemaWriter>(DatabaseTypes.SqlServer);

        // Register keyed data readers
        services.AddKeyedSingleton<IDataReader, SqlServerDataReader>(DatabaseTypes.SqlServer);
        services.AddKeyedSingleton<IDataReader, MySqlDataReader>(DatabaseTypes.MySql);
        services.AddKeyedSingleton<IDataReader, OracleDataReader>(DatabaseTypes.Oracle);
        services.AddKeyedSingleton<IDataReader, PostgresDataReader>(DatabaseTypes.PostgreSql);

        // Register keyed data writers
        services.AddKeyedSingleton<IDataWriter, PostgresDataWriter>(DatabaseTypes.PostgreSql);
        services.AddKeyedSingleton<IDataWriter, MySqlDataWriter>(DatabaseTypes.MySql);
        services.AddKeyedSingleton<IDataWriter, OracleDataWriter>(DatabaseTypes.Oracle);
        services.AddKeyedSingleton<IDataWriter, SqlServerDataWriter>(DatabaseTypes.SqlServer);

        // Register SQL collector for dry run support
        services.AddSingleton<ISqlCollector>(sp =>
            new SqlCollector(_options.DryRun.Enabled, _options.DryRun.IncludeComments));

        // Register services
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
