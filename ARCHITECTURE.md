# SchemaForge Architecture

## High-Level Overview

SchemaForge is a cross-database migration tool built on a **plugin-based architecture**. The core library contains orchestration logic and shared services, while database-specific implementations live in separate provider assemblies that are discovered at runtime.

```
┌──────────────────────────────────────────────────────────────┐
│                     Entry Points                              │
│   SchemaForge.Cli (CLI)          DbMigrate (Fluent API)      │
└──────────────┬───────────────────────────┬───────────────────┘
               │                           │
               ▼                           ▼
┌──────────────────────────────────────────────────────────────┐
│                    SchemaForge (Core)                         │
│                                                              │
│  MigrationOrchestrator ─── coordinates 6-step workflow       │
│  MigrationBuilder ──────── fluent configuration + DI setup   │
│  BulkDataMigrator ──────── batched data transfer             │
│  AssemblyPluginLoader ──── runtime provider discovery        │
│                                                              │
│  Shared Services:                                            │
│    SnakeCaseConverter, UniversalDataTypeMapper,               │
│    SqlDialectConverter, SqlCollector,                         │
│    DatabaseStandardsProvider, TableDependencySorter           │
└──────────────┬───────────────────────────────────────────────┘
               │ depends on
               ▼
┌──────────────────────────────────────────────────────────────┐
│               SchemaForge.Abstractions                        │
│                                                              │
│  Interfaces: IDatabaseProvider, ISchemaReader, ISchemaWriter, │
│              IDataReader, IDataWriter, INamingConverter, ...  │
│  Models: TableSchema, ColumnSchema, IndexSchema, ...         │
│  Configuration: MigrationSettings, MigrationOptions          │
└──────────────────────────────────────────────────────────────┘
               ▲ implement
               │
┌──────────────────────────────────────────────────────────────┐
│                   Provider Plugins                            │
│                                                              │
│  SchemaForge.Providers.SqlServer                             │
│  SchemaForge.Providers.Postgres                              │
│  SchemaForge.Providers.MySql                                 │
│  SchemaForge.Providers.Oracle                                │
│                                                              │
│  Each provider contains:                                     │
│    - XxxProvider : IDatabaseProvider (self-registration)      │
│    - XxxSchemaReader : ISchemaReader                          │
│    - XxxSchemaWriter : ISchemaWriter                          │
│    - XxxDataReader : IDataReader                              │
│    - XxxDataWriter : IDataWriter                              │
└──────────────────────────────────────────────────────────────┘
```

## Project Structure

```
SchemaForge/
├── src/
│   ├── SchemaForge.Abstractions/     # Interfaces, models, contracts
│   │   ├── Interfaces/
│   │   │   ├── IDatabaseProvider.cs   # Plugin contract
│   │   │   ├── ISchemaReader.cs       # Read schema from source DB
│   │   │   ├── ISchemaWriter.cs       # Write schema to target DB
│   │   │   ├── IDataReader.cs         # Read data from source DB
│   │   │   ├── IDataWriter.cs         # Write data to target DB
│   │   │   └── ...                    # INamingConverter, ISqlCollector, etc.
│   │   └── Models/
│   │       ├── TableSchema.cs         # Table + column definitions
│   │       ├── IndexSchema.cs         # Index definitions
│   │       ├── ConstraintSchema.cs    # CHECK, UNIQUE, DEFAULT
│   │       ├── ViewSchema.cs          # View definitions
│   │       ├── MigrationSettings.cs   # Configuration model
│   │       └── MigrationOptions.cs    # Migration option flags
│   │
│   ├── SchemaForge/                   # Core library (no DB-specific code)
│   │   ├── Builder/
│   │   │   ├── DbMigrate.cs           # Static fluent API entry point
│   │   │   └── MigrationBuilder.cs    # Fluent builder + DI container setup
│   │   └── Services/
│   │       ├── MigrationOrchestrator.cs    # 6-step workflow coordinator
│   │       ├── BulkDataMigrator.cs         # Batched data transfer
│   │       ├── AssemblyPluginLoader.cs     # Runtime provider discovery
│   │       ├── SnakeCaseConverter.cs       # Naming convention converter
│   │       ├── UniversalDataTypeMapper.cs  # Cross-DB type mapping
│   │       ├── SqlDialectConverter.cs      # SQL expression conversion
│   │       ├── SqlCollector.cs             # Dry run SQL collector
│   │       ├── DatabaseStandardsProvider.cs # DB convention lookups
│   │       └── TableDependencySorter.cs    # FK dependency ordering
│   │
│   ├── SchemaForge.Cli/              # CLI entry point
│   │   ├── Program.cs                # System.CommandLine parsing
│   │   └── appsettings.template.json # Configuration template
│   │
│   └── SchemaForge.Providers.*/      # Database plugins
│       ├── SchemaForge.Providers.SqlServer/
│       ├── SchemaForge.Providers.Postgres/
│       ├── SchemaForge.Providers.MySql/
│       └── SchemaForge.Providers.Oracle/
│
├── tests/
│   ├── SchemaForge.Tests/            # Unit tests (xUnit + Moq)
│   ├── run-tests.sh                  # Integration test runner
│   └── seed-sqlserver.sql            # Integration test seed data
│
├── SchemaForge.sln
├── docker-compose.test.yml           # Integration test containers
├── ARCHITECTURE.md                   # This file
├── DESIGN_PATTERNS.md                # Design patterns documentation
└── TESTING.md                        # Testing guide
```

## Plugin System

### How Providers Register

Each provider assembly contains an `IDatabaseProvider` implementation that self-registers its services:

```csharp
public interface IDatabaseProvider
{
    string ProviderKey { get; }  // e.g., "sqlserver", "postgres"
    void Register(IServiceCollection services);
}
```

During startup, `AssemblyPluginLoader` scans all loaded assemblies for types implementing `IDatabaseProvider` and calls `Register()` on each:

```csharp
public static void LoadProviders(IServiceCollection services)
{
    var providerTypes = AppDomain.CurrentDomain.GetAssemblies()
        .SelectMany(a => a.GetTypes())
        .Where(t => typeof(IDatabaseProvider).IsAssignableFrom(t) && !t.IsAbstract);

    foreach (var type in providerTypes)
    {
        var provider = (IDatabaseProvider)Activator.CreateInstance(type)!;
        provider.Register(services);
    }
}
```

### Keyed Dependency Injection

Providers register their services using .NET 8+ keyed DI, with the provider key (e.g., `"postgres"`) as the service key:

```csharp
// Inside PostgresProvider.Register()
services.AddKeyedTransient<ISchemaReader, PostgresSchemaReader>(ProviderKey);
services.AddKeyedTransient<ISchemaWriter, PostgresSchemaWriter>(ProviderKey);
services.AddKeyedTransient<IDataReader, PostgresDataReader>(ProviderKey);
services.AddKeyedTransient<IDataWriter, PostgresDataWriter>(ProviderKey);
```

At runtime, services are resolved by key:

```csharp
var reader = sp.GetRequiredKeyedService<ISchemaReader>(settings.SourceDatabaseType);
var writer = sp.GetRequiredKeyedService<ISchemaWriter>(settings.TargetDatabaseType);
```

## Migration Workflow

The `MigrationOrchestrator` coordinates a 6-step migration process. Each step can be independently enabled/disabled via `MigrationOptions`:

```
Step 1: Read Source Schema
    ISchemaReader.ReadTablesAsync()
    ISchemaReader.ReadIndexesAsync()
    ISchemaReader.ReadConstraintsAsync()
    ISchemaReader.ReadViewsAsync()
        │
        ▼
Step 2: Create Tables (if MigrateSchema = true)
    TableDependencySorter.Sort()  ← topological sort by FK dependencies
    ISchemaWriter.CreateSchemaAsync()
        │
        ▼
Step 3: Migrate Data (if MigrateData = true)
    BulkDataMigrator.MigrateDataAsync()
        IDataWriter.DisableConstraintsAsync()
        for each table:
            IDataReader.GetRowCountAsync()
            loop: IDataReader.FetchBatchAsync() → IDataWriter.BulkInsertAsync()
            IDataWriter.ResetSequencesAsync()
        IDataWriter.EnableConstraintsAsync()
        │
        ▼
Step 4: Create Indexes (if MigrateIndexes = true)
    ISchemaWriter.CreateIndexesAsync()
        │
        ▼
Step 5: Create Constraints (if MigrateConstraints = true)
    ISchemaWriter.CreateConstraintsAsync()
        │
        ▼
Step 6: Create Views (if MigrateViews = true)
    ISchemaWriter.CreateViewsAsync()
```

## Dry Run Mode

When dry run is enabled, the `SqlCollector` intercepts all SQL generation. SchemaWriters accept an `ISqlCollector` and call `AddSql()` instead of executing against a database. The collector accumulates statements categorized by type (Tables, Indexes, Constraints, Views, ForeignKeys, Data).

```
Normal mode:   SchemaWriter → Database connection → Execute SQL
Dry run mode:  SchemaWriter → SqlCollector → Accumulate SQL → Output script
```

This pattern also enables unit testing of SchemaWriters without any database connection.

## Data Flow

### Type Mapping Pipeline

```
Source DB Column Type (e.g., "nvarchar(max)")
        │
        ▼
NormalizeSourceType()          ← strips precision: TIMESTAMP(6) → timestamp
        │
        ▼
UniversalDataTypeMapper        ← maps: nvarchar(max) → text (postgres)
        │
        ▼
Target DDL Column Type (e.g., "text")
```

### SQL Dialect Conversion Pipeline

```
Source SQL Expression (e.g., "SELECT GETDATE(), ISNULL(x, 0)")
        │
        ▼
SqlDialectConverter            ← function mapping: GETDATE() → NOW()
                               ← null-check: ISNULL() → COALESCE()
                               ← identifier quoting: [col] → "col"
                               ← boolean literals: = 1 → = TRUE
        │
        ▼
Target SQL Expression (e.g., "SELECT NOW(), COALESCE(x, 0)")
```

### Naming Convention Pipeline

```
Source Identifier (e.g., "OrderHeaders")
        │
        ▼
DatabaseStandardsProvider      ← lookup target convention
        │
        ▼
SnakeCaseConverter             ← apply convention based on target DB
        │
        ▼
Target Identifier:
    PostgreSQL → "order_headers"
    SQL Server → "OrderHeaders"
    MySQL      → "orderheaders"
    Oracle     → "ORDERHEADERS"
```

## Configuration Resolution

Two entry points converge on the same `MigrationOrchestrator`:

```
CLI Path:                              Fluent API Path:
  System.CommandLine                     DbMigrate.FromSqlServer("...")
  → Parse CLI args                       → MigrationBuilder
  → Merge with appsettings.json          → Accumulate settings
  → Build ServiceCollection              → Build ServiceCollection
  → Resolve MigrationOrchestrator        → Resolve MigrationOrchestrator
  → ExecuteAsync()                       → ExecuteAsync()
```

**Config resolution order (CLI):** CLI args > environment variables > appsettings.json > defaults

## Key Design Decisions

1. **Plugin-based providers**: Adding a new database requires only a new provider assembly implementing `IDatabaseProvider`. No changes to core code.

2. **Keyed DI over factory pattern**: Leverages .NET 8+ keyed services for cleaner resolution than manual factory switches.

3. **SqlCollector for testability**: SchemaWriters can be fully unit tested by passing `SqlCollector(isCollecting: true)` and inspecting generated SQL.

4. **Separate Abstractions project**: Prevents circular dependencies and keeps the contract layer clean for both core and providers.

5. **Data migration with constraint toggling**: Constraints are disabled before data transfer and re-enabled after, wrapped in try/finally to ensure cleanup even on failure.
