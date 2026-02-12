# SchemaForge - Technical Design & Patterns

## Architecture Overview

```
CLI (System.CommandLine)          DbMigrate (Static Factory)
    |                                 |
    v                                 v
Program.cs (ParseResult)         MigrationBuilder (Builder + Fluent Interface)
    |                                 |
    +---------- both build -----------+
                    |
                    v
          MigrationOrchestrator (Orchestrator)
    |
    +---> ISchemaReader (Strategy) ---> TableSchema (Common Model)
    |                                        |
    |                                        v
    +---> ISchemaWriter (Strategy) <--- TableSchema
    |
    +---> IDataMigrator (Strategy)
    |         |
    |         +---> IDataReader (Strategy)
    |         +---> IDataWriter (Strategy)
    |
    +---> ISqlCollector (Collector) --- Dry Run Mode
    |
    +---> Supporting Services:
          +---> IDataTypeMapper (Adapter)
          +---> ISqlDialectConverter (Adapter)
          +---> INamingConverter (Strategy)
          +---> IDatabaseStandardsProvider (Provider)
          +---> TableDependencySorter (Algorithm)
```

## Design Patterns Used

### 1. Builder Pattern
**Class:** `MigrationBuilder`

Separates construction of a complex migration configuration from its execution. Accumulates settings in `_settings` and `_options`, then builds the full service graph on `ExecuteAsync()`.

```csharp
DbMigrate.FromSqlServer("...")
    .ToPostgres("...", "public")
    .MigrateAll()
    .WithBatchSize(5000)
    .DryRun()
    .ExecuteAsync();
```

### 2. Static Factory Pattern
**Class:** `DbMigrate`

Provides a static entry point that hides `MigrationBuilder` construction. Offers convenience methods like `FromSqlServer()`, `FromPostgres()` that return a builder instance.

### 3. Fluent Interface Pattern
**Class:** `MigrationBuilder`

Every configuration method returns `this`, enabling method chaining for readable, expressive configuration.

### 4. Strategy Pattern
**Interfaces:** `ISchemaReader`, `ISchemaWriter`, `IDataReader`, `IDataWriter`, `INamingConverter`

This pattern is implemented via **keyed Dependency Injection (DI) services**. Each database-specific implementation of a strategy (e.g., `PostgresSchemaWriter`, `SqlServerSchemaWriter`) is registered with a unique key corresponding to the database type (e.g., "postgres", "sqlserver").

At runtime, the application resolves the correct strategy dynamically based on the user's configuration. This avoids conditional logic (like `if/switch` statements) and adheres to the **Open/Closed Principle**, as adding support for a new database only requires adding new implementations, not modifying existing factory or orchestration code.

```csharp
// Example: Resolving the ISchemaWriter for the target database
var schemaWriter = serviceProvider.GetRequiredKeyedService<ISchemaWriter>(settings.TargetDatabaseType);

// schemaWriter is now either PostgresSchemaWriter, SqlServerSchemaWriter, etc.
await schemaWriter.CreateTableAsync(...);
```

This is used for all core database interactions:
- `ISchemaReader`: Reading source schema.
- `ISchemaWriter`: Writing target schema.
- `IDataReader`: Reading source data.
- `IDataWriter`: Writing target data.

### 5. Adapter Pattern
**Interfaces:** `IDataTypeMapper`, `ISqlDialectConverter`

Converts (adapts) between incompatible database-specific formats:
- `IDataTypeMapper`: Adapts data types (`varchar` -> `character varying`, `bit` -> `boolean`)
- `ISqlDialectConverter`: Adapts SQL syntax (`GETDATE()` -> `NOW()`, `NEWID()` -> `gen_random_uuid()`)

Also, each `ISchemaReader` acts as an adapter - converting database-specific metadata queries into the common `TableSchema` model.

### 6. Orchestrator Pattern
**Class:** `MigrationOrchestrator`

Coordinates the multi-step migration workflow in the correct order, managing dependencies between steps:
1. Read schema -> 2. Create tables -> 3. Migrate data -> 4. Create indexes -> 5. Create constraints -> 6. Create views

### 7. Options Pattern
**Classes:** `MigrationSettings`, `MigrationOptions`, `DryRunOptions`

Configuration bound to strongly-typed classes via `IOptions<T>`, following the Microsoft.Extensions.Options pattern.

### 8. Dependency Injection
**Location:** `MigrationBuilder.BuildServiceProvider()`, `Program.cs`

All services registered and resolved via `IServiceProvider`. Uses keyed services for database-specific implementations. Two entry points build the DI container:
- **CLI path** (`Program.cs`): `System.CommandLine` parses args, merges with `appsettings.json` fallbacks, builds `ServiceCollection` manually.
- **Fluent API path** (`MigrationBuilder`): Builder accumulates settings, builds `ServiceCollection` in `ExecuteAsync()`.

### 9. Collector Pattern
**Interface:** `ISqlCollector`

Accumulates SQL statements during dry run mode for later output. Conditionally active based on `IsCollecting` flag.

### 10. Provider Pattern
**Interface:** `IDatabaseStandardsProvider`

Supplies database-specific conventions (naming rules, reserved keywords, identifier limits) without performing operations.

## Where Adapter Pattern Is Already Used

The Adapter pattern is already present in two forms:

### Implicit Adapters (SchemaReaders)
Each `ISchemaReader` adapts a database-specific API into the common `TableSchema` model:
```
SqlConnection + sys.tables queries  -->  SqlServerSchemaReader  -->  TableSchema
NpgsqlConnection + pg_catalog       -->  PostgresSchemaReader   -->  TableSchema
MySqlConnection + information_schema -->  MySqlSchemaReader      -->  TableSchema
OracleConnection + ALL_TABLES       -->  OracleSchemaReader     -->  TableSchema
```

### Explicit Adapters (Converters)
- `IDataTypeMapper`: Adapts `int/bigint/varchar` between all 4 database systems
- `ISqlDialectConverter`: Adapts SQL functions, operators, and syntax between dialects
