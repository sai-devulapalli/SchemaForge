
# SchemaForge

A cross-database migration tool for .NET that migrates schemas and data between different database systems.

## Supported Databases

- **SQL Server** (source and target)
- **PostgreSQL** (source and target)
- **MySQL** (source and target)
- **Oracle** (source and target)

## Features

- **Schema Migration** - Tables, columns, primary keys, foreign keys
- **Data Migration** - Bulk data transfer with configurable batch sizes
- **View Migration** - SQL dialect conversion for view definitions
- **Index Migration** - Including unique, filtered, and covering indexes
- **Constraint Migration** - CHECK, UNIQUE, and DEFAULT constraints
- **Naming Convention Conversion** - Automatic conversion between snake_case, PascalCase, etc.
- **Table Dependency Sorting** - Proper ordering based on foreign key relationships
- **Dry Run Mode** - Generate and preview SQL without executing against the target database
- **Configurable Options** - Migrate all or select specific components
- **Fluent API** - Chainable builder pattern for programmatic configuration

## Quick Start

### Prerequisites

- .NET 9.0 SDK or later
- Access to source and target databases

### 1. Clone and build

```bash
git clone https://github.com/your-org/SchemaForge.git
cd SchemaForge
dotnet build
```

### 2. Run via CLI

```bash
# Full migration
schemaforge --from sqlserver --to postgres \
  --source-conn "Server=localhost;Database=SourceDB;User Id=sa;Password=pass;TrustServerCertificate=True" \
  --target-conn "Host=localhost;Database=targetdb;Username=postgres;Password=pass" \
  --schema public

# Or with dotnet run
dotnet run -- --from sqlserver --to postgres \
  --source-conn "Server=localhost;..." --target-conn "Host=localhost;..."
```

### 3. Or configure via appsettings.json

Copy the template and fill in your connection strings:

```bash
cp appsettings.template.json appsettings.json
dotnet run
```

### 4. Or use the fluent API in code

```csharp
using SchemaForge.Builder;

await DbMigrate
    .FromSqlServer("Server=localhost;Database=SourceDB;User Id=sa;Password=pass;TrustServerCertificate=True")
    .ToPostgres("Host=localhost;Database=targetdb;Username=postgres;Password=pass", "public")
    .MigrateAll()
    .ExecuteAsync();
```

### Install as a .NET global tool

```bash
dotnet pack
dotnet tool install --global --add-source ./nupkg SchemaForge
schemaforge --help
```

## CLI Usage

SchemaForge is a full CLI tool powered by `System.CommandLine`. All options can be passed as command-line arguments, with `appsettings.json` and environment variables as fallbacks.

**Config resolution order**: CLI args > environment variables > appsettings.json > defaults

```bash
# Full migration
schemaforge --from sqlserver --to postgres \
  --source-conn "Server=localhost;..." \
  --target-conn "Host=localhost;..." \
  --schema public

# Schema only, dry run to file
schemaforge --from mysql --to postgres \
  --source-conn "..." --target-conn "..." \
  --schema-only --dry-run --dry-run-output migration.sql

# With table filtering
schemaforge --from sqlserver --to mysql \
  --source-conn "..." --target-conn "..." \
  --include-tables Users,Orders --batch-size 5000

# Version
schemaforge --version
```

### CLI Options

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `--from` | string | - | Source DB type: `sqlserver`, `postgres`, `mysql`, `oracle` |
| `--to` | string | - | Target DB type: `sqlserver`, `postgres`, `mysql`, `oracle` |
| `--source-conn` | string | - | Source connection string |
| `--target-conn` | string | - | Target connection string |
| `--schema` | string | `public` | Target schema name |
| `--batch-size` | int | `1000` | Rows per batch |
| `--naming` | string | `auto` | Naming convention |
| `--schema-only` | flag | `false` | Migrate schema without data |
| `--data-only` | flag | `false` | Migrate data only (schema must exist) |
| `--no-views` | flag | `false` | Skip view migration |
| `--no-indexes` | flag | `false` | Skip index migration |
| `--no-constraints` | flag | `false` | Skip constraint migration |
| `--no-foreign-keys` | flag | `false` | Skip FK migration |
| `--include-tables` | string[] | `[]` | Tables to include (comma-separated) |
| `--exclude-tables` | string[] | `[]` | Tables to exclude (comma-separated) |
| `--dry-run` | flag | `false` | Generate SQL without executing |
| `--dry-run-output` | string | - | File path for dry run SQL output |
| `--continue-on-error` | flag | `true` | Continue on failures |
| `--verbose` | flag | `false` | Enable debug logging |
| `--quiet` | flag | `false` | Warnings only |

## Configuration

Create an `appsettings.json` file in the project directory:
```json
{
  "SourceDatabaseType": "sqlserver",
  "TargetDatabaseType": "postgres",
  "SourceConnectionString": "Server=localhost;Database=SourceDB;User Id=sa;Password=YourPassword;TrustServerCertificate=True",
  "TargetConnectionString": "Host=localhost;Database=targetdb;Username=postgres;Password=YourPassword",
  "TargetSchemaName": "public",
  "BatchSize": 1000,
  "NamingConvention": "auto",
  "UseTargetDatabaseStandards": true,
  "PreserveSourceCase": false,
  "MigrationOptions": {
    "MigrateSchema": true,
    "MigrateData": true,
    "MigrateViews": true,
    "MigrateIndexes": true,
    "MigrateConstraints": true,
    "MigrateForeignKeys": true,
    "DataBatchSize": 1000,
    "ContinueOnError": true,
    "IncludeTables": [],
    "ExcludeTables": []
  }
}
```

Connection strings also support environment variable substitution (e.g., `${SOURCE_CONNECTION_STRING}`).

### Configuration Options

| Option | Description | Default |
|--------|-------------|---------|
| `SourceDatabaseType` | Source database: `sqlserver`, `postgres`, `mysql`, `oracle` | `sqlserver` |
| `TargetDatabaseType` | Target database: `sqlserver`, `postgres`, `mysql`, `oracle` | `postgres` |
| `SourceConnectionString` | Connection string for source database | - |
| `TargetConnectionString` | Connection string for target database | - |
| `TargetSchemaName` | Schema name in target database | `public` |
| `BatchSize` | Rows per batch during data migration | `1000` |
| `NamingConvention` | `auto`, `snake_case`, `PascalCase`, `camelCase`, `lowercase`, `UPPERCASE`, `preserve` | `auto` |
| `UseTargetDatabaseStandards` | Apply target database naming conventions | `true` |
| `PreserveSourceCase` | Keep original identifier casing | `false` |
| `MaxIdentifierLength` | Maximum identifier length (0 = target default) | `0` |

### Migration Options

| Option | Description | Default |
|--------|-------------|---------|
| `MigrateSchema` | Create tables and columns | `true` |
| `MigrateData` | Transfer row data | `true` |
| `MigrateViews` | Create views | `true` |
| `MigrateIndexes` | Create indexes | `true` |
| `MigrateConstraints` | Create CHECK, UNIQUE, DEFAULT constraints | `true` |
| `MigrateForeignKeys` | Create foreign key relationships | `true` |
| `ContinueOnError` | Continue if individual objects fail | `true` |
| `IncludeTables` | Only migrate these tables (empty = all) | `[]` |
| `ExcludeTables` | Skip these tables | `[]` |

## Usage

### Run Migration

```bash
# Via CLI arguments
dotnet run -- --from sqlserver --to postgres \
  --source-conn "Server=localhost;..." --target-conn "Host=localhost;..."

# Via appsettings.json (fallback when no CLI args provided)
dotnet run
```

### Predefined Migration Presets

You can use predefined configurations in code:

```csharp
// Full migration (schema + data)
var options = MigrationOptions.Full;

// Schema only (no data)
var options = MigrationOptions.SchemaOnly;

// Data only (assumes schema exists)
var options = MigrationOptions.DataOnly;

// Tables only (no views, indexes, constraints)
var options = MigrationOptions.TablesOnly;
```

### Fluent API

Use the fluent API for programmatic migration configuration:

```csharp
using SchemaForge.Builder;

// Basic migration: SQL Server to PostgreSQL
await DbMigrate
    .FromSqlServer("Server=localhost;Database=SourceDB;...")
    .ToPostgres("Host=localhost;Database=targetdb;...", "public")
    .MigrateAll()
    .ExecuteAsync();

// Schema only migration
await DbMigrate
    .FromSqlServer("...")
    .ToPostgres("...")
    .MigrateSchemaOnly()
    .ExecuteAsync();

// Data only migration (assumes schema exists)
await DbMigrate
    .FromPostgres("...")
    .ToMySql("...")
    .MigrateDataOnly()
    .ExecuteAsync();

// Selective migration with table filtering
await DbMigrate
    .FromSqlServer("...")
    .ToOracle("...", "MYSCHEMA")
    .WithSchema()
    .WithData()
    .WithIndexes()
    .WithoutViews()
    .WithoutConstraints()
    .IncludeTables("Users", "Orders", "Products")
    .ExcludeTables("AuditLog", "TempData")
    .ExecuteAsync();

// Full configuration
await DbMigrate
    .FromSqlServer("...")
    .ToPostgres("...", "app")
    .MigrateAll()
    .WithBatchSize(5000)
    .ContinueOnError()
    .WithNamingConvention("snake_case")
    .ConfigureLogging(logging => logging
        .AddConsole()
        .SetMinimumLevel(LogLevel.Debug))
    .ExecuteAsync();

// Generic database configuration
await DbMigrate
    .From("sqlserver", "...")
    .To("postgres", "...", "public")
    .MigrateAll()
    .ExecuteAsync();
```

#### Fluent API Methods

| Method | Description |
|--------|-------------|
| `FromSqlServer(connectionString)` | Set SQL Server as source |
| `FromPostgres(connectionString)` | Set PostgreSQL as source |
| `FromMySql(connectionString)` | Set MySQL as source |
| `FromOracle(connectionString)` | Set Oracle as source |
| `ToSqlServer(connectionString, schema)` | Set SQL Server as target |
| `ToPostgres(connectionString, schema)` | Set PostgreSQL as target |
| `ToMySql(connectionString, schema)` | Set MySQL as target |
| `ToOracle(connectionString, schema)` | Set Oracle as target |
| `MigrateAll()` | Migrate everything (schema, data, views, indexes, constraints, FKs) |
| `MigrateSchemaOnly()` | Migrate only schema (no data) |
| `MigrateDataOnly()` | Migrate only data (assumes schema exists) |
| `WithSchema()` | Enable schema migration |
| `WithData()` | Enable data migration |
| `WithViews()` | Enable view migration |
| `WithIndexes()` | Enable index migration |
| `WithConstraints()` | Enable constraint migration |
| `WithForeignKeys()` | Enable foreign key migration |
| `WithoutSchema()` | Disable schema migration |
| `WithoutData()` | Disable data migration |
| `WithoutViews()` | Disable view migration |
| `WithoutIndexes()` | Disable index migration |
| `WithoutConstraints()` | Disable constraint migration |
| `WithoutForeignKeys()` | Disable foreign key migration |
| `IncludeTables(params string[])` | Only migrate specified tables |
| `ExcludeTables(params string[])` | Skip specified tables |
| `WithBatchSize(int)` | Set data batch size |
| `ContinueOnError()` | Continue on errors |
| `StopOnError()` | Stop on first error |
| `WithNamingConvention(NamingConvention)` | Set naming convention from enum |
| `PreserveNames()` | Keep original identifier names |
| `WithAutoNaming()` | Auto-detect naming based on target DB |
| `WithMaxIdentifierLength(int)` | Set maximum identifier length (0 = target default) |
| `DryRun()` | Generate SQL without executing |
| `DryRun(outputPath)` | Generate SQL and write to file |
| `WithDataSamples(sampleCount)` | Include sample INSERT statements in dry run |
| `WithoutDataSamples()` | Exclude data samples from dry run output |
| `ExecuteDryRunAsync()` | Execute dry run and return generated SQL |
| `ConfigureLogging(Action)` | Configure logging |
| `WithLogLevel(LogLevel)` | Set minimum log level |
| `Verbose()` | Enable verbose logging |
| `Quiet()` | Minimize logging output |
| `ExecuteAsync()` | Run the migration |

### Dry Run Mode

Preview all generated SQL before executing against a live database. Dry run mode collects every DDL/DML statement and returns them without making any changes.

```csharp
// Preview SQL to console
await DbMigrate
    .FromSqlServer("...")
    .ToPostgres("...", "public")
    .MigrateAll()
    .DryRun()
    .ExecuteAsync();

// Write generated SQL to a file
await DbMigrate
    .FromSqlServer("...")
    .ToPostgres("...", "public")
    .MigrateAll()
    .DryRun("/path/to/output.sql")
    .ExecuteAsync();

// Get structured dry run result
DryRunResult result = await DbMigrate
    .FromSqlServer("...")
    .ToPostgres("...", "public")
    .MigrateAll()
    .WithDataSamples(10)
    .ExecuteDryRunAsync();

Console.WriteLine($"Tables: {result.Summary.TableCount}");
Console.WriteLine($"Indexes: {result.Summary.IndexCount}");
Console.WriteLine($"Total statements: {result.Summary.TotalStatements}");
Console.WriteLine(result.Script);
```

#### Dry Run Options

| Option | Description | Default |
|--------|-------------|---------|
| `Enabled` | Generate SQL without executing | `false` |
| `OutputFilePath` | File path to write generated SQL (null = console only) | `null` |
| `IncludeDataSamples` | Include sample INSERT statements | `true` |
| `SampleRowCount` | Number of sample rows per table | `5` |
| `IncludeComments` | Add step headers/comments to output | `true` |

## Migration Process

The migration executes in the following order:

1. **Read Source Schema** - Extract table definitions, columns, keys, indexes, constraints
2. **Sort Tables** - Order by foreign key dependencies (parent tables first)
3. **Create Tables** - Create table structures with primary keys
4. **Migrate Data** - Bulk transfer data in batches (constraints disabled during import)
5. **Create Indexes** - Add indexes after data for better performance
6. **Create Constraints** - Add CHECK, UNIQUE, DEFAULT constraints
7. **Create Views** - Create views with converted SQL definitions
8. **Create Foreign Keys** - Add foreign key relationships

## Data Type Mapping

The tool automatically maps data types between databases:

| SQL Server | PostgreSQL | MySQL | Oracle |
|------------|------------|-------|--------|
| `int` | `integer` | `INT` | `NUMBER(10)` |
| `bigint` | `bigint` | `BIGINT` | `NUMBER(19)` |
| `bit` | `boolean` | `TINYINT(1)` | `NUMBER(3)` |
| `varchar(n)` | `character varying(n)` | `VARCHAR(n)` | `VARCHAR2(n)` |
| `nvarchar(max)` | `text` | `TEXT` | `CLOB` |
| `datetime2` | `timestamp` | `DATETIME` | `TIMESTAMP` |
| `uniqueidentifier` | `uuid` | `CHAR(36)` | `RAW(16)` |
| `varbinary` | `bytea` | `BLOB` | `BLOB` |

## Naming Convention Conversion

When `NamingConvention` is set to `auto`, identifiers are converted to match target database standards:

| Database | Convention | Example |
|----------|------------|---------|
| PostgreSQL | snake_case | `user_accounts` |
| SQL Server | PascalCase | `UserAccounts` |
| MySQL | lowercase | `useraccounts` |
| Oracle | UPPERCASE | `USERACCOUNTS` |

## Project Structure

```
SchemaForge/
├── Builder/
│   ├── DbMigrate.cs               # Static fluent API entry point
│   └── MigrationBuilder.cs        # Fluent builder implementation
├── Configuration/
│   └── MigrationSettings.cs       # Configuration model
├── Models/
│   ├── ColumnSchema.cs            # Column definition
│   ├── TableSchema.cs             # Table definition
│   ├── ForeignKeySchema.cs        # Foreign key definition
│   ├── Schemas.cs                 # Index, View, Constraint schemas
│   ├── DatabaseDialect.cs         # SQL dialect definitions
│   ├── MigrationOptions.cs        # Migration options
│   ├── DryRunOptions.cs           # Dry run configuration
│   └── DryRunResult.cs            # Dry run output model
├── Services/
│   ├── Interfaces/                # Service contracts
│   ├── SchemaReader/              # Read schema from databases
│   ├── SchemaWriter/              # Write schema to databases
│   ├── DataReader/                # Read data from databases
│   ├── DataWriter/                # Write data to databases
│   ├── MigrationOrchestrator.cs   # Main migration coordinator
│   ├── BulkDataMigrator.cs        # Data migration service
│   ├── TableDependencySorter.cs   # FK dependency ordering
│   ├── SnakeCaseConverter.cs      # Naming conversion
│   ├── UniversalDataTypeMapper.cs # Type mapping
│   ├── SqlDialectConverter.cs     # SQL expression conversion
│   ├── SqlCollector.cs            # Dry run SQL collector
│   └── DatabaseStandardsProvider.cs # DB conventions
├── Examples/                      # Usage examples
├── Program.cs                     # Entry point & DI setup
├── SchemaForge.csproj             # Project file
├── appsettings.json               # Configuration
├── appsettings.template.json      # Configuration template
└── DESIGN_PATTERNS.md             # Architecture & design documentation
```

## Architecture

SchemaForge uses a strategy-based architecture with keyed dependency injection. Each database has dedicated implementations for schema reading, schema writing, data reading, and data writing. The `MigrationOrchestrator` coordinates the full migration workflow.

For a detailed breakdown of design patterns and architectural decisions, see [DESIGN_PATTERNS.md](DESIGN_PATTERNS.md).

## Requirements

- .NET 9.0 or later
- Access to source and target databases

## NuGet Packages

- `Microsoft.Data.SqlClient` - SQL Server connectivity
- `Npgsql` - PostgreSQL connectivity
- `MySql.Data` - MySQL connectivity
- `Oracle.ManagedDataAccess.Core` - Oracle connectivity
- `Microsoft.Extensions.Configuration` - Configuration management
- `Microsoft.Extensions.Configuration.EnvironmentVariables` - Environment variable configuration provider
- `Microsoft.Extensions.Configuration.Json` - JSON configuration provider
- `Microsoft.Extensions.DependencyInjection` - Dependency injection
- `Microsoft.Extensions.Logging` - Logging abstractions
- `Microsoft.Extensions.Logging.Console` - Console logging provider
- `Microsoft.Extensions.Options.ConfigurationExtensions` - Options configuration support
- `System.CommandLine` - Command-line argument parsing

## Limitations

- Stored procedures, functions, and triggers are not migrated
- Spatial data types require manual handling
- Some complex view definitions may need manual adjustment
- Circular foreign key dependencies are handled but may require manual verification

## License

MIT License