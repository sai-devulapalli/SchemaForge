using Microsoft.Extensions.Logging;
using SchemaForge.Builder;
using Xunit;
using Xunit.Abstractions;

namespace SchemaForge.PackageTests;

/// <summary>
/// Integration tests that exercise the SchemaForge NuGet package via the fluent API.
/// These tests require the Docker containers from docker-compose.test.yml to be running
/// and SQL Server to be seeded with the test data from tests/seed-sqlserver.sql.
///
/// Run via: bash tests/run-package-tests.sh
/// </summary>
[Collection("PackageTests")]
public class MigrationLibraryTests
{
    private readonly ITestOutputHelper _output;

    private const int ExpectedTables = 5;
    private const int ExpectedRows   = 48; // 5+10+10+8+15

    public MigrationLibraryTests(ITestOutputHelper output)
    {
        _output = output;
    }

    // ----------------------------------------------------------------
    // Shared seed helper — seeds any non-SQL Server DB from SQL Server
    // ----------------------------------------------------------------

    private async Task SeedFromSqlServerAsync(Func<Task> cleanTarget, Func<Task> migrate)
    {
        await cleanTarget();
        await migrate();
    }

    // ----------------------------------------------------------------
    // SQL Server → PostgreSQL
    // ----------------------------------------------------------------

    [Fact]
    public async Task SqlServer_To_Postgres_SchemaAndData_Succeeds()
    {
        await DatabaseHelper.CleanPostgresAsync(ConnectionStrings.Postgres);

        await DbMigrate
            .FromSqlServer(ConnectionStrings.SqlServer)
            .ToPostgres(ConnectionStrings.Postgres)
            .WithoutViews().WithoutIndexes().WithoutConstraints().WithoutForeignKeys()
            .WithBatchSize(500)
            .ContinueOnError()
            .WithLogLevel(LogLevel.Warning)
            .ExecuteAsync();

        var tables = await DatabaseHelper.GetPostgresTableCountAsync(ConnectionStrings.Postgres);
        var depts  = await DatabaseHelper.GetPostgresRowCountAsync(ConnectionStrings.Postgres, "public.departments");
        var emps   = await DatabaseHelper.GetPostgresRowCountAsync(ConnectionStrings.Postgres, "public.employees");
        var prods  = await DatabaseHelper.GetPostgresRowCountAsync(ConnectionStrings.Postgres, "public.products");
        var orders = await DatabaseHelper.GetPostgresRowCountAsync(ConnectionStrings.Postgres, "public.order_headers");
        var details= await DatabaseHelper.GetPostgresRowCountAsync(ConnectionStrings.Postgres, "public.order_details");
        var total  = depts + emps + prods + orders + details;

        _output.WriteLine($"tables={tables}, rows={total} (dept={depts} emp={emps} prod={prods} oh={orders} od={details})");

        Assert.Equal(ExpectedTables, tables);
        Assert.InRange(total, 40, 55);
        Assert.Equal(5,  depts);
        Assert.Equal(10, emps);
        Assert.Equal(10, prods);
        Assert.Equal(8,  orders);
        Assert.Equal(15, details);
    }

    [Fact]
    public async Task SqlServer_To_Postgres_FullMigration_IncludesViewsAndIndexes()
    {
        await DatabaseHelper.CleanPostgresAsync(ConnectionStrings.Postgres);

        await DbMigrate
            .FromSqlServer(ConnectionStrings.SqlServer)
            .ToPostgres(ConnectionStrings.Postgres)
            .MigrateAll()
            .WithBatchSize(500)
            .ContinueOnError()
            .WithLogLevel(LogLevel.Warning)
            .ExecuteAsync();

        var tables = await DatabaseHelper.GetPostgresTableCountAsync(ConnectionStrings.Postgres);
        var emps   = await DatabaseHelper.GetPostgresRowCountAsync(ConnectionStrings.Postgres, "public.employees");

        // Verify views exist
        await using var pgConn = new Npgsql.NpgsqlConnection(ConnectionStrings.Postgres);
        await pgConn.OpenAsync();
        await using var viewCmd = pgConn.CreateCommand();
        viewCmd.CommandText = "SELECT COUNT(*) FROM information_schema.views WHERE table_schema='public'";
        var views = Convert.ToInt32(await viewCmd.ExecuteScalarAsync() ?? 0);

        // Verify indexes exist
        await using var idxCmd = pgConn.CreateCommand();
        idxCmd.CommandText = "SELECT COUNT(*) FROM pg_indexes WHERE schemaname='public' AND indexname NOT LIKE '%_pkey'";
        var indexes = Convert.ToInt32(await idxCmd.ExecuteScalarAsync() ?? 0);

        _output.WriteLine($"tables={tables}, employees={emps}, views={views}, indexes={indexes}");

        Assert.Equal(ExpectedTables, tables);
        Assert.Equal(10, emps);
        Assert.True(views >= 1, $"Expected at least 1 view, got {views}");
        Assert.True(indexes >= 1, $"Expected at least 1 index, got {indexes}");
    }

    // ----------------------------------------------------------------
    // SQL Server → MySQL
    // ----------------------------------------------------------------

    [Fact]
    public async Task SqlServer_To_MySql_SchemaAndData_Succeeds()
    {
        await DatabaseHelper.CleanMySqlAsync(ConnectionStrings.MySql);

        await DbMigrate
            .FromSqlServer(ConnectionStrings.SqlServer)
            .ToMySql(ConnectionStrings.MySql, "schemaforge_test")
            .WithoutViews().WithoutIndexes().WithoutConstraints().WithoutForeignKeys()
            .WithBatchSize(500)
            .ContinueOnError()
            .WithLogLevel(LogLevel.Warning)
            .ExecuteAsync();

        var tables = await DatabaseHelper.GetMySqlTableCountAsync(ConnectionStrings.MySql);
        var depts  = await DatabaseHelper.GetMySqlRowCountAsync(ConnectionStrings.MySql, "schemaforge_test.departments");
        var emps   = await DatabaseHelper.GetMySqlRowCountAsync(ConnectionStrings.MySql, "schemaforge_test.employees");
        var prods  = await DatabaseHelper.GetMySqlRowCountAsync(ConnectionStrings.MySql, "schemaforge_test.products");
        var orders = await DatabaseHelper.GetMySqlRowCountAsync(ConnectionStrings.MySql, "schemaforge_test.order_headers");
        var details= await DatabaseHelper.GetMySqlRowCountAsync(ConnectionStrings.MySql, "schemaforge_test.order_details");
        var total  = depts + emps + prods + orders + details;

        _output.WriteLine($"tables={tables}, rows={total} (dept={depts} emp={emps} prod={prods} oh={orders} od={details})");

        Assert.Equal(ExpectedTables, tables);
        Assert.InRange(total, 40, 55);
        Assert.Equal(5,  depts);
        Assert.Equal(10, emps);
        Assert.Equal(10, prods);
        Assert.Equal(8,  orders);
        Assert.Equal(15, details);
    }

    // ----------------------------------------------------------------
    // DryRun — no database writes
    // ----------------------------------------------------------------

    [Fact]
    public async Task SqlServer_To_Postgres_DryRun_ReturnsSql_WithoutWriting()
    {
        await DatabaseHelper.CleanPostgresAsync(ConnectionStrings.Postgres);

        var result = await DbMigrate
            .FromSqlServer(ConnectionStrings.SqlServer)
            .ToPostgres(ConnectionStrings.Postgres)
            .MigrateAll()
            .WithLogLevel(LogLevel.Warning)
            .ExecuteDryRunAsync();

        // Dry run should produce SQL but not touch the target DB
        Assert.NotNull(result);
        Assert.NotEmpty(result.Statements);
        Assert.Contains(result.Statements, s => s.Sql.Contains("CREATE TABLE", StringComparison.OrdinalIgnoreCase));

        // Target should still be empty
        var tables = await DatabaseHelper.GetPostgresTableCountAsync(ConnectionStrings.Postgres);
        Assert.Equal(0, tables);

        _output.WriteLine($"DryRun produced {result.Statements.Count} SQL statements");
    }

    // ----------------------------------------------------------------
    // Selective table migration
    // ----------------------------------------------------------------

    [Fact]
    public async Task SqlServer_To_Postgres_IncludeTablesFilter_MigratesSubset()
    {
        await DatabaseHelper.CleanPostgresAsync(ConnectionStrings.Postgres);

        await DbMigrate
            .FromSqlServer(ConnectionStrings.SqlServer)
            .ToPostgres(ConnectionStrings.Postgres)
            .IncludeTables("Departments", "Employees")
            .WithoutViews().WithoutIndexes().WithoutConstraints().WithoutForeignKeys()
            .WithLogLevel(LogLevel.Warning)
            .ExecuteAsync();

        var tables = await DatabaseHelper.GetPostgresTableCountAsync(ConnectionStrings.Postgres);
        var depts  = await DatabaseHelper.GetPostgresRowCountAsync(ConnectionStrings.Postgres, "public.departments");
        var emps   = await DatabaseHelper.GetPostgresRowCountAsync(ConnectionStrings.Postgres, "public.employees");

        _output.WriteLine($"tables={tables}, departments={depts}, employees={emps}");

        Assert.Equal(2, tables);
        Assert.Equal(5,  depts);
        Assert.Equal(10, emps);
    }

    // ----------------------------------------------------------------
    // Schema-only migration
    // ----------------------------------------------------------------

    [Fact]
    public async Task SqlServer_To_MySql_SchemaOnly_CreatesTablesWithNoRows()
    {
        await DatabaseHelper.CleanMySqlAsync(ConnectionStrings.MySql);

        await DbMigrate
            .FromSqlServer(ConnectionStrings.SqlServer)
            .ToMySql(ConnectionStrings.MySql, "schemaforge_test")
            .MigrateSchemaOnly()
            .WithLogLevel(LogLevel.Warning)
            .ExecuteAsync();

        var tables = await DatabaseHelper.GetMySqlTableCountAsync(ConnectionStrings.MySql);
        var depts  = await DatabaseHelper.GetMySqlRowCountAsync(ConnectionStrings.MySql, "schemaforge_test.departments");

        _output.WriteLine($"tables={tables}, departments={depts}");

        Assert.Equal(ExpectedTables, tables);
        Assert.Equal(0, depts);  // schema only — no data
    }

    // ----------------------------------------------------------------
    // Postgres → MySQL (non-SQL Server source)
    // ----------------------------------------------------------------

    [Fact]
    public async Task Postgres_To_MySql_SchemaAndData_Succeeds()
    {
        // Seed Postgres first from SQL Server
        await DatabaseHelper.CleanPostgresAsync(ConnectionStrings.Postgres);
        await DbMigrate
            .FromSqlServer(ConnectionStrings.SqlServer)
            .ToPostgres(ConnectionStrings.Postgres)
            .WithoutViews().WithoutIndexes().WithoutConstraints().WithoutForeignKeys()
            .WithLogLevel(LogLevel.Warning)
            .ExecuteAsync();

        // Now migrate Postgres → MySQL
        await DatabaseHelper.CleanMySqlAsync(ConnectionStrings.MySql);
        await DbMigrate
            .FromPostgres(ConnectionStrings.Postgres)
            .ToMySql(ConnectionStrings.MySql, "schemaforge_test")
            .WithoutViews().WithoutIndexes().WithoutConstraints().WithoutForeignKeys()
            .WithBatchSize(500)
            .ContinueOnError()
            .WithLogLevel(LogLevel.Warning)
            .ExecuteAsync();

        var tables = await DatabaseHelper.GetMySqlTableCountAsync(ConnectionStrings.MySql);
        var depts  = await DatabaseHelper.GetMySqlRowCountAsync(ConnectionStrings.MySql, "schemaforge_test.departments");
        var emps   = await DatabaseHelper.GetMySqlRowCountAsync(ConnectionStrings.MySql, "schemaforge_test.employees");
        var total  = depts + emps
                   + await DatabaseHelper.GetMySqlRowCountAsync(ConnectionStrings.MySql, "schemaforge_test.products")
                   + await DatabaseHelper.GetMySqlRowCountAsync(ConnectionStrings.MySql, "schemaforge_test.order_headers")
                   + await DatabaseHelper.GetMySqlRowCountAsync(ConnectionStrings.MySql, "schemaforge_test.order_details");

        _output.WriteLine($"tables={tables}, rows={total}");

        Assert.Equal(ExpectedTables, tables);
        Assert.InRange(total, 40, 55);
    }

    // ----------------------------------------------------------------
    // MySQL → SQL Server
    // ----------------------------------------------------------------

    [Fact]
    public async Task MySql_To_SqlServer_SchemaAndData_Succeeds()
    {
        // Seed MySQL from SQL Server
        await DatabaseHelper.CleanMySqlAsync(ConnectionStrings.MySql);
        await DbMigrate
            .FromSqlServer(ConnectionStrings.SqlServer)
            .ToMySql(ConnectionStrings.MySql, "schemaforge_test")
            .WithoutViews().WithoutIndexes().WithoutConstraints().WithoutForeignKeys()
            .WithLogLevel(LogLevel.Warning)
            .ExecuteAsync();

        // Migrate MySQL → SQL Server (into a separate schema to avoid collision)
        await DatabaseHelper.CleanSqlServerSchemaAsync(ConnectionStrings.SqlServer, "mysql_target");
        await DbMigrate
            .FromMySql(ConnectionStrings.MySql)
            .ToSqlServer(ConnectionStrings.SqlServer, "mysql_target")
            .WithoutViews().WithoutIndexes().WithoutConstraints().WithoutForeignKeys()
            .WithBatchSize(500)
            .ContinueOnError()
            .WithLogLevel(LogLevel.Warning)
            .ExecuteAsync();

        var tables = await DatabaseHelper.GetSqlServerTableCountAsync(ConnectionStrings.SqlServer, "mysql_target");
        var rows   = await DatabaseHelper.GetSqlServerTotalRowsAsync(ConnectionStrings.SqlServer, "mysql_target");

        _output.WriteLine($"tables={tables}, rows={rows}");

        Assert.Equal(ExpectedTables, tables);
        Assert.InRange(rows, 40, 55);
    }

    // ----------------------------------------------------------------
    // MySQL → PostgreSQL
    // ----------------------------------------------------------------

    [Fact]
    public async Task MySql_To_Postgres_SchemaAndData_Succeeds()
    {
        // Seed MySQL from SQL Server
        await DatabaseHelper.CleanMySqlAsync(ConnectionStrings.MySql);
        await DbMigrate
            .FromSqlServer(ConnectionStrings.SqlServer)
            .ToMySql(ConnectionStrings.MySql, "schemaforge_test")
            .WithoutViews().WithoutIndexes().WithoutConstraints().WithoutForeignKeys()
            .WithLogLevel(LogLevel.Warning)
            .ExecuteAsync();

        // Migrate MySQL → Postgres
        await DatabaseHelper.CleanPostgresAsync(ConnectionStrings.Postgres);
        await DbMigrate
            .FromMySql(ConnectionStrings.MySql)
            .ToPostgres(ConnectionStrings.Postgres)
            .WithoutViews().WithoutIndexes().WithoutConstraints().WithoutForeignKeys()
            .WithBatchSize(500)
            .ContinueOnError()
            .WithLogLevel(LogLevel.Warning)
            .ExecuteAsync();

        var tables = await DatabaseHelper.GetPostgresTableCountAsync(ConnectionStrings.Postgres);
        var depts  = await DatabaseHelper.GetPostgresRowCountAsync(ConnectionStrings.Postgres, "public.departments");
        var emps   = await DatabaseHelper.GetPostgresRowCountAsync(ConnectionStrings.Postgres, "public.employees");
        var total  = depts + emps
                   + await DatabaseHelper.GetPostgresRowCountAsync(ConnectionStrings.Postgres, "public.products")
                   + await DatabaseHelper.GetPostgresRowCountAsync(ConnectionStrings.Postgres, "public.order_headers")
                   + await DatabaseHelper.GetPostgresRowCountAsync(ConnectionStrings.Postgres, "public.order_details");

        _output.WriteLine($"tables={tables}, rows={total}");

        Assert.Equal(ExpectedTables, tables);
        Assert.InRange(total, 40, 55);
    }

    // ----------------------------------------------------------------
    // PostgreSQL → SQL Server
    // ----------------------------------------------------------------

    [Fact]
    public async Task Postgres_To_SqlServer_SchemaAndData_Succeeds()
    {
        // Seed Postgres from SQL Server
        await DatabaseHelper.CleanPostgresAsync(ConnectionStrings.Postgres);
        await DbMigrate
            .FromSqlServer(ConnectionStrings.SqlServer)
            .ToPostgres(ConnectionStrings.Postgres)
            .WithoutViews().WithoutIndexes().WithoutConstraints().WithoutForeignKeys()
            .WithLogLevel(LogLevel.Warning)
            .ExecuteAsync();

        // Migrate Postgres → SQL Server (separate schema)
        await DatabaseHelper.CleanSqlServerSchemaAsync(ConnectionStrings.SqlServer, "pg_target");
        await DbMigrate
            .FromPostgres(ConnectionStrings.Postgres)
            .ToSqlServer(ConnectionStrings.SqlServer, "pg_target")
            .WithoutViews().WithoutIndexes().WithoutConstraints().WithoutForeignKeys()
            .WithBatchSize(500)
            .ContinueOnError()
            .WithLogLevel(LogLevel.Warning)
            .ExecuteAsync();

        var tables = await DatabaseHelper.GetSqlServerTableCountAsync(ConnectionStrings.SqlServer, "pg_target");
        var rows   = await DatabaseHelper.GetSqlServerTotalRowsAsync(ConnectionStrings.SqlServer, "pg_target");

        _output.WriteLine($"tables={tables}, rows={rows}");

        Assert.Equal(ExpectedTables, tables);
        Assert.InRange(rows, 40, 55);
    }

    // ----------------------------------------------------------------
    // PostgreSQL → MySQL
    // ----------------------------------------------------------------

    [Fact]
    public async Task Postgres_To_MySql_Succeeds()
    {
        // Seed Postgres from SQL Server
        await DatabaseHelper.CleanPostgresAsync(ConnectionStrings.Postgres);
        await DbMigrate
            .FromSqlServer(ConnectionStrings.SqlServer)
            .ToPostgres(ConnectionStrings.Postgres)
            .WithoutViews().WithoutIndexes().WithoutConstraints().WithoutForeignKeys()
            .WithLogLevel(LogLevel.Warning)
            .ExecuteAsync();

        // Migrate Postgres → MySQL
        await DatabaseHelper.CleanMySqlAsync(ConnectionStrings.MySql);
        await DbMigrate
            .FromPostgres(ConnectionStrings.Postgres)
            .ToMySql(ConnectionStrings.MySql, "schemaforge_test")
            .WithoutViews().WithoutIndexes().WithoutConstraints().WithoutForeignKeys()
            .WithBatchSize(500)
            .ContinueOnError()
            .WithLogLevel(LogLevel.Warning)
            .ExecuteAsync();

        var tables = await DatabaseHelper.GetMySqlTableCountAsync(ConnectionStrings.MySql);
        var total  = await DatabaseHelper.GetMySqlRowCountAsync(ConnectionStrings.MySql, "schemaforge_test.departments")
                   + await DatabaseHelper.GetMySqlRowCountAsync(ConnectionStrings.MySql, "schemaforge_test.employees")
                   + await DatabaseHelper.GetMySqlRowCountAsync(ConnectionStrings.MySql, "schemaforge_test.products")
                   + await DatabaseHelper.GetMySqlRowCountAsync(ConnectionStrings.MySql, "schemaforge_test.order_headers")
                   + await DatabaseHelper.GetMySqlRowCountAsync(ConnectionStrings.MySql, "schemaforge_test.order_details");

        _output.WriteLine($"tables={tables}, rows={total}");

        Assert.Equal(ExpectedTables, tables);
        Assert.InRange(total, 40, 55);
    }

    // ----------------------------------------------------------------
    // MySQL → MySQL (same DB, different schema — round-trip sanity)
    // ----------------------------------------------------------------

    [Fact]
    public async Task SqlServer_To_Postgres_To_MySql_RoundTrip_Succeeds()
    {
        // SQL Server → Postgres
        await DatabaseHelper.CleanPostgresAsync(ConnectionStrings.Postgres);
        await DbMigrate
            .FromSqlServer(ConnectionStrings.SqlServer)
            .ToPostgres(ConnectionStrings.Postgres)
            .WithoutViews().WithoutIndexes().WithoutConstraints().WithoutForeignKeys()
            .WithBatchSize(500)
            .ContinueOnError()
            .WithLogLevel(LogLevel.Warning)
            .ExecuteAsync();

        // Diagnostic: verify PG received data
        var pgDepts = await DatabaseHelper.GetPostgresRowCountAsync(ConnectionStrings.Postgres, "public.departments");
        var pgEmps  = await DatabaseHelper.GetPostgresRowCountAsync(ConnectionStrings.Postgres, "public.employees");
        var pgProds = await DatabaseHelper.GetPostgresRowCountAsync(ConnectionStrings.Postgres, "public.products");
        var pgOH    = await DatabaseHelper.GetPostgresRowCountAsync(ConnectionStrings.Postgres, "public.order_headers");
        var pgOD    = await DatabaseHelper.GetPostgresRowCountAsync(ConnectionStrings.Postgres, "public.order_details");
        _output.WriteLine($"Hop1 SS→PG: dept={pgDepts} emp={pgEmps} prod={pgProds} oh={pgOH} od={pgOD}");

        // Postgres → MySQL
        await DatabaseHelper.CleanMySqlAsync(ConnectionStrings.MySql);
        await DbMigrate
            .FromPostgres(ConnectionStrings.Postgres)
            .ToMySql(ConnectionStrings.MySql, "schemaforge_test")
            .WithoutViews().WithoutIndexes().WithoutConstraints().WithoutForeignKeys()
            .WithBatchSize(500)
            .ContinueOnError()
            .WithLogLevel(LogLevel.Warning)
            .ExecuteAsync();

        // Diagnostic: verify MySQL received data
        var myDepts = await DatabaseHelper.GetMySqlRowCountAsync(ConnectionStrings.MySql, "schemaforge_test.departments");
        var myEmps  = await DatabaseHelper.GetMySqlRowCountAsync(ConnectionStrings.MySql, "schemaforge_test.employees");
        var myProds = await DatabaseHelper.GetMySqlRowCountAsync(ConnectionStrings.MySql, "schemaforge_test.products");
        var myOH    = await DatabaseHelper.GetMySqlRowCountAsync(ConnectionStrings.MySql, "schemaforge_test.order_headers");
        var myOD    = await DatabaseHelper.GetMySqlRowCountAsync(ConnectionStrings.MySql, "schemaforge_test.order_details");
        _output.WriteLine($"Hop2 PG→MySQL: dept={myDepts} emp={myEmps} prod={myProds} oh={myOH} od={myOD}");

        // MySQL → SQL Server (back to origin)
        await DatabaseHelper.CleanSqlServerSchemaAsync(ConnectionStrings.SqlServer, "roundtrip");
        await DbMigrate
            .FromMySql(ConnectionStrings.MySql)
            .ToSqlServer(ConnectionStrings.SqlServer, "roundtrip")
            .WithoutViews().WithoutIndexes().WithoutConstraints().WithoutForeignKeys()
            .WithBatchSize(500)
            .ContinueOnError()
            .WithLogLevel(LogLevel.Warning)
            .ExecuteAsync();

        var tables = await DatabaseHelper.GetSqlServerTableCountAsync(ConnectionStrings.SqlServer, "roundtrip");
        var rows   = await DatabaseHelper.GetSqlServerTotalRowsAsync(ConnectionStrings.SqlServer, "roundtrip");

        _output.WriteLine($"Round-trip: tables={tables}, rows={rows}");

        Assert.Equal(ExpectedTables, tables);
        Assert.InRange(rows, 40, 55);
    }
}
