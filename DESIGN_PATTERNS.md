# SchemaForge - Technical Design & Patterns

## Architecture Overview

```
DbMigrate (Static Factory)
    |
    v
MigrationBuilder (Builder + Fluent Interface)
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

Multiple interchangeable implementations selected at runtime based on database type:
- `ISchemaWriter` -> `PostgresSchemaWriter`, `SqlServerSchemaWriter`, `MySqlSchemaWriter`, `OracleSchemaWriter`
- `ISchemaReader` -> `PostgresSchemaReader`, `SqlServerSchemaReader`, `MySqlSchemaReader`, `OracleSchemaReader`

Selected via DI keyed services: `GetRequiredKeyedService<ISchemaWriter>("postgres")`

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

All services registered and resolved via `IServiceProvider`. Uses keyed services for database-specific implementations.

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

## Is Adapter Pattern the Right Next Step?

### Current Problem
Each database currently requires **5 separate classes**:
- `SchemaReader` - reads metadata
- `SchemaWriter` - generates/executes DDL
- `DataReader` - reads row data
- `DataWriter` - writes row data
- Plus shared converters (DataTypeMapper, DialectConverter)

The `BulkDataMigrator` uses type-checking to pair readers/writers:
```csharp
private IDataReader GetDataReader(ISchemaReader schemaReader)
{
    return schemaReader switch
    {
        SqlServerSchemaReader => new SqlServerDataReader(),
        MySqlSchemaReader => new MySqlDataReader(),
        ...
    };
}
```
This is a code smell - it breaks the Open/Closed principle.

### Recommended: Database Adapter
A unified `IDatabaseAdapter` would consolidate all database-specific behavior:

```csharp
public interface IDatabaseAdapter
{
    // Schema operations
    Task<List<TableSchema>> ReadSchemaAsync(string connectionString, ...);
    string GenerateCreateTableSql(string schemaName, TableSchema table);
    string GenerateCreateIndexSql(string schemaName, IndexSchema index);

    // Data operations
    Task<DataTable> FetchBatchAsync(string connectionString, TableSchema table, int offset, int limit);
    Task BulkInsertAsync(string connectionString, string schemaName, TableSchema table, DataTable data);

    // Dialect
    string QuoteIdentifier(string identifier);
    string MapDataType(ColumnSchema column);
}
```

### Trade-offs

| Approach | Pros | Cons |
|----------|------|------|
| **Current (Strategy)** | Clear separation of concerns, each class is small | 5 classes per database (20 total), type-checking in BulkDataMigrator |
| **Database Adapter** | One class per database (4 total), no type-checking needed, easier to add new databases | Larger classes, mixes read/write concerns |
| **Hybrid** | Keep ISchemaReader/ISchemaWriter, but add IDatabaseAdapter for data operations only | Moderate complexity, solves the type-checking smell |

### Verdict
The **Adapter pattern is a good fit** for the data layer (`IDataReader`/`IDataWriter`) where the type-checking smell exists. For schema operations, the current Strategy pattern works well since `ISchemaReader` and `ISchemaWriter` have clearly different responsibilities. A hybrid approach would be the best next step.