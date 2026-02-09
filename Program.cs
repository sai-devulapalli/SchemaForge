// =============================================================================
// SchemaForge - Cross-Database Migration Tool
// =============================================================================
// This tool migrates database schemas and data between different database systems.
// Supported databases: SQL Server, PostgreSQL, MySQL, and Oracle.
//
// Features:
// - Schema migration (tables, columns, primary keys, foreign keys)
// - Data migration with batch processing
// - View migration with SQL dialect conversion
// - Index and constraint migration
// - Configurable naming conventions (snake_case, PascalCase, etc.)
// - Table dependency sorting for proper foreign key handling
// =============================================================================

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

// Build configuration from appsettings.json and environment variables
var configuration = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
    .AddEnvironmentVariables()
    .Build();

// Create service collection for dependency injection
var services = new ServiceCollection();

// Configure logging to output to console
services.AddLogging(builder =>
{
    builder.AddConsole();
    builder.SetMinimumLevel(LogLevel.Information);
});

// Bind configuration settings to MigrationSettings class
services.Configure<MigrationSettings>(options =>
{
    configuration.Bind(options);

    // Explicitly handle potential swapping of connection strings if environment variables are mismatched
    // This is a common issue when setting up different database types for source and target
    if (options.SourceDatabaseType.Equals(DatabaseTypes.SqlServer, StringComparison.OrdinalIgnoreCase) &&
        options.TargetDatabaseType.Equals(DatabaseTypes.PostgreSql, StringComparison.OrdinalIgnoreCase))
    {
        // If sourceConnectionString contains "Host=" (typical for Postgres)
        // AND targetConnectionString contains "Server=" (typical for SQL Server)
        // then they are likely swapped.
        if (options.SourceConnectionString.Contains("Host=", StringComparison.OrdinalIgnoreCase) &&
            options.TargetConnectionString.Contains("Server=", StringComparison.OrdinalIgnoreCase))
        {
            var logger = services.BuildServiceProvider().GetRequiredService<ILogger<MigrationOrchestrator>>();
            logger.LogWarning("Detected swapped source and target connection strings based on database types and connection string keywords. Swapping them for correction.");
            string temp = options.SourceConnectionString;
            options.SourceConnectionString = options.TargetConnectionString;
            options.TargetConnectionString = temp;
        }
    }
});

// Configure migration options - controls what gets migrated (schema, data, views, etc.)
services.Configure<MigrationOptions>(options =>
{
    // Bind from configuration if available
    configuration.GetSection("MigrationOptions").Bind(options);

    // Or use predefined configurations:
    // var preset = MigrationOptions.Full;        // Schema + Data (default)
    // var preset = MigrationOptions.SchemaOnly;  // Schema only, no data
    // var preset = MigrationOptions.DataOnly;    // Data only, assumes schema exists
    // var preset = MigrationOptions.TablesOnly;  // Tables + data, no views/indexes/constraints
});

// Register schema readers for each supported source database type
// These read table definitions, columns, indexes, constraints from source DB
services.AddKeyedSingleton<ISchemaReader, SqlServerSchemaReader>(DatabaseTypes.SqlServer);
services.AddKeyedSingleton<ISchemaReader, MySqlSchemaReader>(DatabaseTypes.MySql);
services.AddKeyedSingleton<ISchemaReader, OracleSchemaReader>(DatabaseTypes.Oracle);
services.AddKeyedSingleton<ISchemaReader, PostgresSchemaReader>(DatabaseTypes.PostgreSql);

// Register schema writers for each supported target database type
// These create tables, indexes, constraints in the target DB
services.AddKeyedSingleton<ISchemaWriter, PostgresSchemaWriter>(DatabaseTypes.PostgreSql);
services.AddKeyedSingleton<ISchemaWriter, MySqlSchemaWriter>(DatabaseTypes.MySql);
services.AddKeyedSingleton<ISchemaWriter, OracleSchemaWriter>(DatabaseTypes.Oracle);
services.AddKeyedSingleton<ISchemaWriter, SqlServerSchemaWriter>(DatabaseTypes.SqlServer);

// Register keyed data readers for each supported source database type
services.AddKeyedSingleton<IDataReader, SqlServerDataReader>(DatabaseTypes.SqlServer);
services.AddKeyedSingleton<IDataReader, MySqlDataReader>(DatabaseTypes.MySql);
services.AddKeyedSingleton<IDataReader, OracleDataReader>(DatabaseTypes.Oracle);
services.AddKeyedSingleton<IDataReader, PostgresDataReader>(DatabaseTypes.PostgreSql);

// Register keyed data writers for each supported target database type
services.AddKeyedSingleton<IDataWriter, PostgresDataWriter>(DatabaseTypes.PostgreSql);
services.AddKeyedSingleton<IDataWriter, MySqlDataWriter>(DatabaseTypes.MySql);
services.AddKeyedSingleton<IDataWriter, OracleDataWriter>(DatabaseTypes.Oracle);
services.AddKeyedSingleton<IDataWriter, SqlServerDataWriter>(DatabaseTypes.SqlServer);

// Register the data migrator that handles bulk data transfer between databases
services.AddSingleton<IDataMigrator, BulkDataMigrator>();

// Register utility services for naming conversion, SQL dialect conversion, and type mapping
services.AddSingleton<INamingConverter, SnakeCaseConverter>();
services.AddSingleton<ISqlDialectConverter, SqlDialectConverter>();
services.AddSingleton<IDataTypeMapper, UniversalDataTypeMapper>();

// Register database standards provider for naming conventions and identifier rules
services.AddSingleton<IDatabaseStandardsProvider, DatabaseStandardsProvider>();
// Register dependency sorter for ordering tables by foreign key relationships
services.AddSingleton<TableDependencySorter>();
// Register SQL collector for dry run support (disabled by default in Program.cs)
services.AddSingleton<ISqlCollector>(sp => new SqlCollector(isCollecting: false));
// Register the main orchestrator that coordinates the entire migration process
services.AddSingleton<MigrationOrchestrator>();

// Build the service provider and ensure proper disposal (Fix #10)
await using var serviceProvider = services.BuildServiceProvider();

// Get the orchestrator and execute the migration
var orchestrator = serviceProvider.GetRequiredService<MigrationOrchestrator>();
await orchestrator.ExecuteMigrationAsync();
