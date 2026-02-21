using Microsoft.Extensions.Logging;
using SchemaForge.Builder;

// ── Connection strings ──────────────────────────────────────────────────────
var srcConn   = "Server=localhost,14330;Database=WideWorldImporters;User Id=sa;Password=StrongPassword@123;TrustServerCertificate=True;";
var tgtConn   = "Host=localhost;Port=5432;Database=postgres;Username=postgres;Password=postgres_password";
var tgtSchema = "wwi";

// ── Parse args ──────────────────────────────────────────────────────────────
bool dryRun     = args.Contains("--dry-run");
bool schemaOnly = args.Contains("--schema-only");

Console.WriteLine("=== WideWorldImporters → PostgreSQL Migration ===");
Console.WriteLine($"Source : SQL Server  (localhost:14330, WideWorldImporters)");
Console.WriteLine($"Target : PostgreSQL  (localhost:5432, postgres, schema={tgtSchema})");
Console.WriteLine($"Mode   : {(dryRun ? "DRY RUN  (SQL generated, nothing written)" : schemaOnly ? "SCHEMA ONLY  (tables, views, indexes – no data)" : "FULL  (schema + data + views + indexes + constraints)")}");
Console.WriteLine();

var migration = DbMigrate
    .FromSqlServer(srcConn)
    .ToPostgres(tgtConn, tgtSchema)
    .WithSourceSchema("*")            // read ALL non-system schemas (Application, Sales, Purchasing, Warehouse, …)
    .WithLogLevel(LogLevel.Information)
    .ContinueOnError();               // log failures but keep going

if (dryRun)
{
    var result = await migration
        .MigrateAll()
        .DryRun()
        .ExecuteAsync();

    if (result != null)
    {
        Console.WriteLine();
        Console.WriteLine("=== Generated SQL Preview ===");
        Console.WriteLine($"Tables      : {result.Summary.TableCount}");
        Console.WriteLine($"Indexes     : {result.Summary.IndexCount}");
        Console.WriteLine($"Constraints : {result.Summary.ConstraintCount}");
        Console.WriteLine($"Views       : {result.Summary.ViewCount}");
        Console.WriteLine($"Total stmts : {result.Summary.TotalStatements}");
        Console.WriteLine();
        Console.WriteLine(result.Script);
    }
}
else if (schemaOnly)
{
    await migration
        .MigrateSchemaOnly()
        .ExecuteAsync();
}
else
{
    await migration
        .MigrateAll()
        .ExecuteAsync();
}

Console.WriteLine();
Console.WriteLine("Done.");
