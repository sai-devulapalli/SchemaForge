
# SchemaForge Testing Guide

## Overview

SchemaForge uses end-to-end integration tests to validate all 12 cross-database migration paths across 4 supported databases: **SQL Server**, **PostgreSQL**, **MySQL**, and **Oracle**. The tests run against real database instances in Docker containers.

## Prerequisites

- **Docker** (with Docker Compose v2)
- **.NET 9.0 SDK**
- **bash** (macOS/Linux; the script is compatible with bash 3.2+)
- ~4 GB free RAM for the 4 database containers
- ~10 minutes for a full test run

## Quick Start

```bash
# Run all tests (starts containers, runs tests, stops containers)
bash tests/run-tests.sh

# Run tests and keep containers running for debugging
bash tests/run-tests.sh --keep-containers

# Skip Oracle tests (faster, useful if Oracle image is unavailable)
bash tests/run-tests.sh --skip-oracle
```

## Test Infrastructure

### Docker Containers (`docker-compose.test.yml`)

| Database    | Image                          | Host Port | Internal Port | Database/Schema      |
|-------------|--------------------------------|-----------|---------------|----------------------|
| SQL Server  | `mcr.microsoft.com/mssql/server:2022-latest` | 1434 | 1433 | `schemaforge_test` (dbo) |
| PostgreSQL  | `postgres:16`                  | 5433      | 5432          | `schemaforge_test` (public) |
| MySQL       | `mysql:8.0`                    | 3307      | 3306          | `schemaforge_test`   |
| Oracle      | `gvenzl/oracle-free:23-slim`   | 1522      | 1521          | `FREEPDB1` (TESTUSER) |

Ports are offset from defaults to avoid conflicts with local database instances.

### Credentials

| Database   | User       | Password            |
|------------|------------|---------------------|
| SQL Server | `sa`       | `SchemaForge@Test1` |
| PostgreSQL | `postgres` | `SchemaForgeTest1`  |
| MySQL      | `root`     | `SchemaForgeTest1`  |
| Oracle     | `testuser` | `SchemaForgeTest1`  |

### Seed Data (`tests/seed-sqlserver.sql`)

The seed script creates 5 tables in SQL Server with test data covering various scenarios:

| Table          | Rows | Purpose                                       |
|----------------|------|-----------------------------------------------|
| `Departments`  | 5    | Root table, IDENTITY PK, NVARCHAR, DECIMAL, BIT, DATETIME, NVARCHAR(MAX) |
| `Employees`    | 10   | FK to Departments, DATE, FLOAT, NULL values, special characters (O'Brien), zero salary |
| `Products`     | 10   | Root table, IDENTITY PK, NULL Description, discontinued flag |
| `OrderHeaders` | 8    | FK to Employees, NULL ShippingAddress, various statuses |
| `OrderDetails` | 15   | FKs to OrderHeaders + Products, DECIMAL with discount values |

**Total: 48 rows across 5 tables**

Additional schema objects in the seed:
- **Views**: `vw_EmployeeDepartments` (JOIN), `vw_OrderSummary` (JOIN + GROUP BY + COUNT)
- **Indexes**: `IX_Employees_Email` (UNIQUE), `IX_Products_SKU`, `IX_OrderDetails_OrderProduct` (composite)
- **Constraints**: `CK_Employees_Salary` (CHECK >= 0), `UQ_Departments_Code` (UNIQUE)

### Data Types Covered

| Category       | SQL Server Types Used                              |
|----------------|-----------------------------------------------------|
| Integer        | `INT` (IDENTITY), `INT` (FK)                        |
| Decimal        | `DECIMAL(15,2)`, `DECIMAL(12,2)`, `DECIMAL(10,2)`, `DECIMAL(5,2)` |
| Floating point | `FLOAT`                                             |
| String         | `NVARCHAR(50-500)`, `VARCHAR(10-100)`, `NVARCHAR(MAX)` |
| Boolean        | `BIT`                                               |
| Date/Time      | `DATETIME`, `DATE`                                  |

### Edge Cases in Test Data

| Scenario              | Location                              |
|-----------------------|---------------------------------------|
| NULL text column      | `Departments.Notes` (row 3), `Products.Description` (row 8) |
| NULL numeric column   | `Employees.Rating` (row 10)           |
| NULL address          | `OrderHeaders.ShippingAddress` (row 4) |
| Zero numeric value    | `Employees.Salary` = 0.00 (row 10)    |
| Special characters    | `Employees.LastName` = "O'Brien" (row 10) |
| Inactive/boolean flag | `Departments.IsActive` = 0 (row 5), `Employees.IsFullTime` = 0 (rows 4, 10) |
| Discontinued product  | `Products.IsDiscontinued` = 1 (row 9), `StockQuantity` = 0 |

## Test Phases

### Phase 1: Schema + Data Migration (12 tests)

Tests all 12 source-to-target migration combinations:

```
sqlserver -> postgres    sqlserver -> mysql    sqlserver -> oracle
postgres  -> sqlserver   postgres  -> mysql    postgres  -> oracle
mysql     -> sqlserver   mysql     -> postgres  mysql     -> oracle
oracle    -> sqlserver   oracle    -> postgres  oracle    -> mysql
```

**For each test:**
1. Seed SQL Server with test data
2. If source is not SQL Server, seed the source by migrating from SQL Server
3. Clean the target database
4. Run SchemaForge migration (schema + data only)
5. Validate: table count = 5, total row count = 48

**Validation criteria:**
- Exactly 5 tables created in target
- Exactly 48 rows total (5 + 10 + 10 + 8 + 15)

### Phase 2: Data Value Validation (4 tests)

Runs after Phase 1. Seeds each target from SQL Server and performs spot-checks:

| Check # | What it validates                              |
|---------|------------------------------------------------|
| 1       | Department "Engineering" exists                |
| 2       | Employee email `alice.johnson@example.com` exists |
| 3       | Special character: `O'Brien` in LastName       |
| 4       | NULL handling: at least 1 NULL Rating          |
| 5       | NULL handling: at least 1 NULL Description     |
| 6       | NULL handling: at least 1 NULL ShippingAddress |
| 7       | Zero value: at least 1 employee with Salary=0 |
| 8       | Per-table row counts: 5, 10, 10, 8, 15        |

### Phase 3: Extended Migration (3 tests)

Tests full migration with views, indexes, and constraints enabled. Runs `sqlserver -> {postgres, mysql, oracle}` with all migration flags:

```json
{
  "MigrateViews": true,
  "MigrateIndexes": true,
  "MigrateConstraints": true,
  "MigrateForeignKeys": true
}
```

**Validation criteria:**
- Tables and rows match (same as Phase 1)
- At least 1 view created in target
- At least 1 non-PK index created in target

## Test Process Flow

```
┌─────────────────────────────────────────────┐
│           Start Test Suite                   │
└─────────────┬───────────────────────────────┘
              │
              ▼
┌─────────────────────────────────────────────┐
│  docker compose up -d                        │
│  Wait for all 4 databases to be healthy      │
└─────────────┬───────────────────────────────┘
              │
              ▼
┌─────────────────────────────────────────────┐
│  Phase 1: 12 Migration Path Tests           │
│                                              │
│  For each (source, target) pair:             │
│    1. Seed SQL Server                        │
│    2. Seed source (if not SQL Server)        │
│    3. Clean target                           │
│    4. Write appsettings.json                 │
│    5. dotnet run (schema + data)             │
│    6. Validate tables=5, rows=48             │
└─────────────┬───────────────────────────────┘
              │
              ▼
┌─────────────────────────────────────────────┐
│  Phase 2: Data Value Validation             │
│                                              │
│  For each target DB:                         │
│    1. Seed from SQL Server                   │
│    2. Spot-check 8 data integrity checks     │
└─────────────┬───────────────────────────────┘
              │
              ▼
┌─────────────────────────────────────────────┐
│  Phase 3: Extended Migration                 │
│                                              │
│  sqlserver -> {postgres, mysql, oracle}:     │
│    1. Migrate with all flags enabled         │
│    2. Validate views, indexes created        │
└─────────────┬───────────────────────────────┘
              │
              ▼
┌─────────────────────────────────────────────┐
│  Print Summary (PASS/FAIL/SKIP)             │
│  docker compose down (unless --keep)         │
└─────────────────────────────────────────────┘
```

## Naming Conventions

SchemaForge auto-converts names based on target database conventions:

| Target     | Convention   | Example Input     | Example Output       |
|------------|-------------|-------------------|----------------------|
| SQL Server | PascalCase  | `order_headers`   | `OrderHeaders`       |
| PostgreSQL | snake_case  | `OrderHeaders`    | `order_headers`      |
| MySQL      | lowercase   | `OrderHeaders`    | `orderheaders`       |
| Oracle     | UPPERCASE   | `order_headers`   | `ORDERHEADERS`       |

The data validation tests account for this by using database-specific table and column names.

## Log Files

All migration logs are saved to `tests/logs/`:

| File Pattern                        | Description                     |
|------------------------------------|---------------------------------|
| `{source}_to_{target}.log`         | Phase 1 migration log            |
| `seed_{source}.log`                | Source seeding log (non-SQL-Server) |
| `dataval_sqlserver_to_{target}.log`| Phase 2 data validation migration log |
| `extended_sqlserver_to_{target}.log`| Phase 3 extended migration log  |

## Debugging Failures

### 1. Check the log file

```bash
cat tests/logs/oracle_to_postgres.log
```

Look for `fail:` or `Exception` lines in the output.

### 2. Keep containers running

```bash
bash tests/run-tests.sh --keep-containers
```

Then connect to any database manually:

```bash
# SQL Server
docker compose -f docker-compose.test.yml exec sqlserver \
  /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "SchemaForge@Test1" -C -d schemaforge_test

# PostgreSQL
docker compose -f docker-compose.test.yml exec postgres \
  psql -U postgres -d schemaforge_test

# MySQL
docker compose -f docker-compose.test.yml exec mysql \
  mysql -uroot -pSchemaForgeTest1 schemaforge_test

# Oracle
docker compose -f docker-compose.test.yml exec oracle \
  sqlplus testuser/SchemaForgeTest1@localhost:1521/FREEPDB1
```

### 3. Run a single migration manually

```bash
# Edit appsettings.json with desired source/target
dotnet run
```

### 4. Common failure patterns

| Error | Likely Cause |
|-------|-------------|
| `ORA-03076: unexpected item GENERATED` | Identity column syntax wrong (nullable before GENERATED) |
| `ORA-00932: LONG incompatible with CHAR` | Oracle LONG columns need `InitialLONGFetchSize = -1` |
| `Unmapped data type 'X'` | Missing type in `UniversalDataTypeMapper` |
| `incorrect binary data format` | CLR type mismatch in PostgreSQL COPY binary (e.g., decimal vs int) |
| `Incorrect date value` | Date format issue in MySqlDataWriter |

## Bugs Fixed During Testing

The following bugs were discovered and fixed during the 12-path test campaign:

| # | Bug | Root Cause | Fix |
|---|-----|-----------|-----|
| 1 | MySQL row count validation incorrect | InnoDB `TABLE_ROWS` is an estimate | Use `COUNT(*)` queries |
| 2 | mysql -> postgres: Boolean/Smallint mismatch | MySQL `tinyint(1)` returns `System.Boolean` | Convert bool to short in `PostgresDataWriter.ConvertValue` |
| 3 | postgres -> mysql: Incorrect date format | `DateOnly`/`DateTime` not formatted for MySQL | Add `ConvertToMySqlType` method |
| 4 | mysql -> postgres: `double` type unmapped | MySQL reports `double` not `double precision` | Add `"double"` to all type mappers |
| 5 | Oracle XE crashes on ARM64 | `gvenzl/oracle-xe:21-slim` no ARM64 support | Switch to `gvenzl/oracle-free:23-slim` |
| 6 | sqlserver -> oracle: `ORA-03076` | Identity clause after NOT NULL | Reorder: `{dataType}{identity} {nullable}` |
| 7 | postgres -> oracle: `ORA-00932` TIMESTAMP/CLOB | `timestamp without time zone` not in Oracle mapper | Add PostgreSQL-specific type names to all mappers |
| 8 | oracle -> *: `ORA-00932` LONG/CHAR | `SEARCH_CONDITION` and `DATA_DEFAULT` are LONG columns | Set `InitialLONGFetchSize = -1`, filter in C# |
| 9 | oracle -> *: `TIMESTAMP(6)` unmapped | Oracle reports `TIMESTAMP(6)` not `TIMESTAMP` | Add `NormalizeSourceType` to strip precision |
| 10 | oracle -> *: `BINARY_DOUBLE` unmapped | Oracle float type not in mappers | Add `binary_double`/`binary_float` to all mappers |
| 11 | oracle -> postgres: binary format error | Oracle `NUMBER(10)` returns `decimal`, PG expects `int` | Convert decimal to correct CLR type in `ConvertValue` |

## Architecture Notes

### Type Mapping Pipeline

```
Source DB Column Type
        │
        ▼
NormalizeSourceType()          ← strips TIMESTAMP(6) → timestamp, etc.
        │
        ▼
MapTo{Target}Type()            ← UniversalDataTypeMapper switch expression
        │
        ▼
Target DDL Column Type
```

### Data Conversion Pipeline

```
Source DB Value (CLR type)
        │
        ▼
ConvertValue() / ConvertTo{Target}Type()   ← handles bool→int, decimal→int, date formatting
        │
        ▼
Target DB Parameter
```

### Key Files

| File | Purpose |
|------|---------|
| `Services/UniversalDataTypeMapper.cs` | Maps column types between all 4 databases |
| `Services/SchemaWriter/*.cs` | Generates DDL for each target database |
| `Services/DataWriter/*.cs` | Handles data insertion for each target database |
| `Services/SchemaReader/*.cs` | Reads schema metadata from each source database |
| `Services/MigrationOrchestrator.cs` | Orchestrates the 6-step migration process |
| `Services/BulkDataMigrator.cs` | Manages batch data transfer between databases |
| `Services/TableDependencySorter.cs` | Topological sort of tables by FK dependencies |
