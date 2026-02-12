using System.CommandLine;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SchemaForge.Configuration;
using SchemaForge.Services;
using SchemaForge.Services.Interfaces;
using SchemaForge.Services.DataReader;
using SchemaForge.Services.DataWriter;
using SchemaForge.Services.SchemaReader;
using SchemaForge.Services.SchemaWriter;
using SchemaForge.Models;

// Build base configuration from appsettings.json and environment variables
var configuration = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
    .AddEnvironmentVariables()
    .Build();

// Read config-based defaults for fallback
var configSettings = new MigrationSettings();
configuration.Bind(configSettings);
var configOptions = new MigrationOptions();
configuration.GetSection("MigrationOptions").Bind(configOptions);

// Define CLI options
var fromOption = new Option<string?>("--from") { Description = "Source database type: sqlserver, postgres, mysql, oracle" };
var toOption = new Option<string?>("--to") { Description = "Target database type: sqlserver, postgres, mysql, oracle" };
var sourceConnOption = new Option<string?>("--source-conn") { Description = "Source connection string" };
var targetConnOption = new Option<string?>("--target-conn") { Description = "Target connection string" };
var schemaOption = new Option<string?>("--schema") { Description = "Target schema name" };
var batchSizeOption = new Option<int?>("--batch-size") { Description = "Rows per batch during data migration" };
var namingOption = new Option<string?>("--naming") { Description = "Naming convention: auto, snake_case, camelCase, PascalCase, lowercase, UPPERCASE, preserve" };
var schemaOnlyOption = new Option<bool>("--schema-only") { Description = "Migrate schema without data" };
var dataOnlyOption = new Option<bool>("--data-only") { Description = "Migrate data only (schema must already exist)" };
var noViewsOption = new Option<bool>("--no-views") { Description = "Skip view migration" };
var noIndexesOption = new Option<bool>("--no-indexes") { Description = "Skip index migration" };
var noConstraintsOption = new Option<bool>("--no-constraints") { Description = "Skip constraint migration" };
var noForeignKeysOption = new Option<bool>("--no-foreign-keys") { Description = "Skip foreign key migration" };
var includeTablesOption = new Option<string[]>("--include-tables") { Description = "Tables to include (comma-separated)", AllowMultipleArgumentsPerToken = true };
var excludeTablesOption = new Option<string[]>("--exclude-tables") { Description = "Tables to exclude (comma-separated)", AllowMultipleArgumentsPerToken = true };
var dryRunOption = new Option<bool>("--dry-run") { Description = "Generate SQL without executing" };
var dryRunOutputOption = new Option<string?>("--dry-run-output") { Description = "File path for dry run SQL output" };
var continueOnErrorOption = new Option<bool?>("--continue-on-error") { Description = "Continue migration on failures" };
var verboseOption = new Option<bool>("--verbose") { Description = "Enable debug logging" };
var quietOption = new Option<bool>("--quiet") { Description = "Warnings and errors only" };

// Build root command
var rootCommand = new RootCommand("SchemaForge - Cross-database schema and data migration tool");
rootCommand.Options.Add(fromOption);
rootCommand.Options.Add(toOption);
rootCommand.Options.Add(sourceConnOption);
rootCommand.Options.Add(targetConnOption);
rootCommand.Options.Add(schemaOption);
rootCommand.Options.Add(batchSizeOption);
rootCommand.Options.Add(namingOption);
rootCommand.Options.Add(schemaOnlyOption);
rootCommand.Options.Add(dataOnlyOption);
rootCommand.Options.Add(noViewsOption);
rootCommand.Options.Add(noIndexesOption);
rootCommand.Options.Add(noConstraintsOption);
rootCommand.Options.Add(noForeignKeysOption);
rootCommand.Options.Add(includeTablesOption);
rootCommand.Options.Add(excludeTablesOption);
rootCommand.Options.Add(dryRunOption);
rootCommand.Options.Add(dryRunOutputOption);
rootCommand.Options.Add(continueOnErrorOption);
rootCommand.Options.Add(verboseOption);
rootCommand.Options.Add(quietOption);

rootCommand.SetAction(async (parseResult, _) =>
{
    // Read CLI values
    var from = parseResult.GetValue(fromOption);
    var to = parseResult.GetValue(toOption);
    var sourceConn = parseResult.GetValue(sourceConnOption);
    var targetConn = parseResult.GetValue(targetConnOption);
    var schema = parseResult.GetValue(schemaOption);
    var batchSize = parseResult.GetValue(batchSizeOption);
    var naming = parseResult.GetValue(namingOption);
    var schemaOnly = parseResult.GetValue(schemaOnlyOption);
    var dataOnly = parseResult.GetValue(dataOnlyOption);
    var noViews = parseResult.GetValue(noViewsOption);
    var noIndexes = parseResult.GetValue(noIndexesOption);
    var noConstraints = parseResult.GetValue(noConstraintsOption);
    var noForeignKeys = parseResult.GetValue(noForeignKeysOption);
    var includeTables = parseResult.GetValue(includeTablesOption);
    var excludeTables = parseResult.GetValue(excludeTablesOption);
    var dryRun = parseResult.GetValue(dryRunOption);
    var dryRunOutput = parseResult.GetValue(dryRunOutputOption);
    var continueOnError = parseResult.GetValue(continueOnErrorOption);
    var verbose = parseResult.GetValue(verboseOption);
    var quiet = parseResult.GetValue(quietOption);

    // Merge: CLI args > config file defaults
    var mergedSettings = new MigrationSettings
    {
        SourceDatabaseType = from ?? configSettings.SourceDatabaseType,
        TargetDatabaseType = to ?? configSettings.TargetDatabaseType,
        SourceConnectionString = sourceConn ?? configSettings.SourceConnectionString,
        TargetConnectionString = targetConn ?? configSettings.TargetConnectionString,
        TargetSchemaName = schema ?? configSettings.TargetSchemaName,
        BatchSize = batchSize ?? configSettings.BatchSize,
        NamingConvention = naming ?? configSettings.NamingConvention,
        UseTargetDatabaseStandards = configSettings.UseTargetDatabaseStandards,
        PreserveSourceCase = configSettings.PreserveSourceCase,
        MaxIdentifierLength = configSettings.MaxIdentifierLength
    };

    var mergedOptions = new MigrationOptions
    {
        MigrateSchema = !dataOnly && configOptions.MigrateSchema,
        MigrateData = !schemaOnly && configOptions.MigrateData,
        MigrateViews = !noViews && !dataOnly && configOptions.MigrateViews,
        MigrateIndexes = !noIndexes && !dataOnly && configOptions.MigrateIndexes,
        MigrateConstraints = !noConstraints && !dataOnly && configOptions.MigrateConstraints,
        MigrateForeignKeys = !noForeignKeys && !dataOnly && configOptions.MigrateForeignKeys,
        DataBatchSize = batchSize ?? configOptions.DataBatchSize,
        ContinueOnError = continueOnError ?? configOptions.ContinueOnError,
        IncludeTables = includeTables is { Length: > 0 }
            ? includeTables.SelectMany(t => t.Split(',')).Select(t => t.Trim()).Where(t => t.Length > 0).ToList()
            : configOptions.IncludeTables,
        ExcludeTables = excludeTables is { Length: > 0 }
            ? excludeTables.SelectMany(t => t.Split(',')).Select(t => t.Trim()).Where(t => t.Length > 0).ToList()
            : configOptions.ExcludeTables,
        DryRun = new DryRunOptions
        {
            Enabled = dryRun || configOptions.DryRun.Enabled,
            OutputFilePath = dryRunOutput ?? configOptions.DryRun.OutputFilePath,
            IncludeDataSamples = configOptions.DryRun.IncludeDataSamples,
            SampleRowCount = configOptions.DryRun.SampleRowCount,
            IncludeComments = configOptions.DryRun.IncludeComments
        }
    };

    // If --schema-only, force data off; if --data-only, force schema off
    if (schemaOnly)
    {
        mergedOptions.MigrateData = false;
    }
    if (dataOnly)
    {
        mergedOptions.MigrateSchema = false;
        mergedOptions.MigrateViews = false;
        mergedOptions.MigrateIndexes = false;
        mergedOptions.MigrateConstraints = false;
        mergedOptions.MigrateForeignKeys = false;
    }

    // Determine log level
    var logLevel = LogLevel.Information;
    if (verbose) logLevel = LogLevel.Debug;
    if (quiet) logLevel = LogLevel.Warning;

    // Build DI container
    var services = new ServiceCollection();

    services.AddLogging(builder =>
    {
        builder.AddConsole();
        builder.SetMinimumLevel(logLevel);
    });

    services.Configure<MigrationSettings>(opts =>
    {
        opts.SourceDatabaseType = mergedSettings.SourceDatabaseType;
        opts.TargetDatabaseType = mergedSettings.TargetDatabaseType;
        opts.SourceConnectionString = mergedSettings.SourceConnectionString;
        opts.TargetConnectionString = mergedSettings.TargetConnectionString;
        opts.TargetSchemaName = mergedSettings.TargetSchemaName;
        opts.BatchSize = mergedSettings.BatchSize;
        opts.NamingConvention = mergedSettings.NamingConvention;
        opts.UseTargetDatabaseStandards = mergedSettings.UseTargetDatabaseStandards;
        opts.PreserveSourceCase = mergedSettings.PreserveSourceCase;
        opts.MaxIdentifierLength = mergedSettings.MaxIdentifierLength;
    });

    services.Configure<MigrationOptions>(opts =>
    {
        opts.MigrateSchema = mergedOptions.MigrateSchema;
        opts.MigrateData = mergedOptions.MigrateData;
        opts.MigrateViews = mergedOptions.MigrateViews;
        opts.MigrateIndexes = mergedOptions.MigrateIndexes;
        opts.MigrateConstraints = mergedOptions.MigrateConstraints;
        opts.MigrateForeignKeys = mergedOptions.MigrateForeignKeys;
        opts.DataBatchSize = mergedOptions.DataBatchSize;
        opts.ContinueOnError = mergedOptions.ContinueOnError;
        opts.IncludeTables = mergedOptions.IncludeTables;
        opts.ExcludeTables = mergedOptions.ExcludeTables;
        opts.DryRun = mergedOptions.DryRun;
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

    // Register data readers
    services.AddKeyedSingleton<IDataReader, SqlServerDataReader>(DatabaseTypes.SqlServer);
    services.AddKeyedSingleton<IDataReader, MySqlDataReader>(DatabaseTypes.MySql);
    services.AddKeyedSingleton<IDataReader, OracleDataReader>(DatabaseTypes.Oracle);
    services.AddKeyedSingleton<IDataReader, PostgresDataReader>(DatabaseTypes.PostgreSql);

    // Register data writers
    services.AddKeyedSingleton<IDataWriter, PostgresDataWriter>(DatabaseTypes.PostgreSql);
    services.AddKeyedSingleton<IDataWriter, MySqlDataWriter>(DatabaseTypes.MySql);
    services.AddKeyedSingleton<IDataWriter, OracleDataWriter>(DatabaseTypes.Oracle);
    services.AddKeyedSingleton<IDataWriter, SqlServerDataWriter>(DatabaseTypes.SqlServer);

    // Register services
    services.AddSingleton<IDataMigrator, BulkDataMigrator>();
    services.AddSingleton<INamingConverter, SnakeCaseConverter>();
    services.AddSingleton<ISqlDialectConverter, SqlDialectConverter>();
    services.AddSingleton<IDataTypeMapper, UniversalDataTypeMapper>();
    services.AddSingleton<IDatabaseStandardsProvider, DatabaseStandardsProvider>();
    services.AddSingleton<TableDependencySorter>();
    services.AddSingleton<ISqlCollector>(_ => new SqlCollector(isCollecting: mergedOptions.DryRun.Enabled));
    services.AddSingleton<MigrationOrchestrator>();

    await using var serviceProvider = services.BuildServiceProvider();
    var orchestrator = serviceProvider.GetRequiredService<MigrationOrchestrator>();
    await orchestrator.ExecuteMigrationAsync();
});

var parseResult = rootCommand.Parse(args);
return await parseResult.InvokeAsync();
