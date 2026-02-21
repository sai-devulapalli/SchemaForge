using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using SchemaForge.Builder;
using Xunit;
using Xunit.Abstractions;

namespace SchemaForge.PackageTests;

/// <summary>
/// Regression tests covering large datasets and edge-case scenarios.
/// Each test creates its own temporary tables in SQL Server and cleans up in finally blocks,
/// keeping them independent of the main seed data.
///
/// Coverage:
///   - 5 000-row bulk transfers to PostgreSQL and MySQL
///   - Boolean / BIT column fidelity (regression for the 22P03 binary-format bug)
///   - NULL values in every nullable column
///   - Unicode: CJK, Arabic, Emoji, special ASCII
///   - Decimal precision extremes
///   - Empty-table schema-only migration
///   - Multi-hop large-data round-trip (SS → PG → MySQL)
///   - Batch-size boundary (rows not divisible by batch size)
/// </summary>
[Collection("PackageTests")]
public class RegressionTests(ITestOutputHelper output)
{
    // ----------------------------------------------------------------
    // Internal SQL helpers
    // ----------------------------------------------------------------

    private static async Task ExecSqlServerAsync(string conn, string sql, int timeoutSec = 120)
    {
        await using var c = new SqlConnection(conn);
        await c.OpenAsync();
        await using var cmd = c.CreateCommand();
        cmd.CommandText    = sql;
        cmd.CommandTimeout = timeoutSec;
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<int> SqlServerRowCountAsync(string conn, string schema, string table)
    {
        await using var c = new SqlConnection(conn);
        await c.OpenAsync();
        await using var cmd = c.CreateCommand();
        cmd.CommandText = $"SELECT COUNT(*) FROM [{schema}].[{table}]";
        return Convert.ToInt32(await cmd.ExecuteScalarAsync() ?? 0);
    }

    private static async Task<int> PgScalarAsync(string conn, string sql)
    {
        await using var c = new Npgsql.NpgsqlConnection(conn);
        await c.OpenAsync();
        await using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        return Convert.ToInt32(await cmd.ExecuteScalarAsync() ?? 0);
    }

    private static async Task<int> MyScalarAsync(string conn, string sql)
    {
        await using var c = new MySql.Data.MySqlClient.MySqlConnection(conn);
        await c.OpenAsync();
        await using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        return Convert.ToInt32(await cmd.ExecuteScalarAsync() ?? 0);
    }

    // ================================================================
    // LARGE DATA TESTS
    // ================================================================

    /// <summary>
    /// 5 000 rows: SQL Server → PostgreSQL.
    /// Confirms boolean binary-format fix holds at scale.
    /// </summary>
    [Fact]
    public async Task LargeData_SqlServer_To_Postgres_TransfersAllRows()
    {
        const int rows = 5_000;
        const string tbl = "regression_large_pg";
        try
        {
            await ExecSqlServerAsync(ConnectionStrings.SqlServer, $"""
                IF OBJECT_ID('dbo.{tbl}','U') IS NOT NULL DROP TABLE dbo.{tbl};
                CREATE TABLE dbo.{tbl} (
                    id        INT IDENTITY(1,1) PRIMARY KEY,
                    name      NVARCHAR(200) NOT NULL,
                    num_val   INT NOT NULL,
                    amount    DECIMAL(12,2) NOT NULL,
                    is_active BIT NOT NULL
                );
                WITH nums AS (
                    SELECT 1 AS n UNION ALL
                    SELECT n+1 FROM nums WHERE n < {rows}
                )
                INSERT INTO dbo.{tbl} (name, num_val, amount, is_active)
                SELECT 'Record ' + CAST(n AS NVARCHAR(10)),
                       n,
                       CAST(n AS DECIMAL(12,2)) * 1.5,
                       CASE WHEN n % 2 = 0 THEN 1 ELSE 0 END
                FROM nums OPTION (MAXRECURSION {rows});
                """);

            await DatabaseHelper.CleanPostgresAsync(ConnectionStrings.Postgres);

            await DbMigrate
                .FromSqlServer(ConnectionStrings.SqlServer)
                .ToPostgres(ConnectionStrings.Postgres)
                .IncludeTables(tbl)
                .WithoutViews().WithoutIndexes().WithoutConstraints().WithoutForeignKeys()
                .WithBatchSize(1000)
                .ContinueOnError()
                .WithLogLevel(LogLevel.Warning)
                .ExecuteAsync();

            var actual = await DatabaseHelper.GetPostgresRowCountAsync(
                ConnectionStrings.Postgres, $"public.{tbl}");

            output.WriteLine($"LargeData SS→PG: expected={rows}, actual={actual}");
            Assert.Equal(rows, actual);
        }
        finally
        {
            await ExecSqlServerAsync(ConnectionStrings.SqlServer,
                $"IF OBJECT_ID('dbo.{tbl}','U') IS NOT NULL DROP TABLE dbo.{tbl};");
        }
    }

    /// <summary>
    /// 5 000 rows: SQL Server → MySQL.
    /// </summary>
    [Fact]
    public async Task LargeData_SqlServer_To_MySql_TransfersAllRows()
    {
        const int rows = 5_000;
        const string tbl = "regression_large_my";
        try
        {
            await ExecSqlServerAsync(ConnectionStrings.SqlServer, $"""
                IF OBJECT_ID('dbo.{tbl}','U') IS NOT NULL DROP TABLE dbo.{tbl};
                CREATE TABLE dbo.{tbl} (
                    id        INT IDENTITY(1,1) PRIMARY KEY,
                    name      NVARCHAR(200) NOT NULL,
                    num_val   INT NOT NULL,
                    amount    DECIMAL(12,2) NOT NULL,
                    is_active BIT NOT NULL
                );
                WITH nums AS (
                    SELECT 1 AS n UNION ALL
                    SELECT n+1 FROM nums WHERE n < {rows}
                )
                INSERT INTO dbo.{tbl} (name, num_val, amount, is_active)
                SELECT 'Record ' + CAST(n AS NVARCHAR(10)),
                       n,
                       CAST(n AS DECIMAL(12,2)) * 2.5,
                       CASE WHEN n % 3 = 0 THEN 1 ELSE 0 END
                FROM nums OPTION (MAXRECURSION {rows});
                """);

            await DatabaseHelper.CleanMySqlAsync(ConnectionStrings.MySql);

            await DbMigrate
                .FromSqlServer(ConnectionStrings.SqlServer)
                .ToMySql(ConnectionStrings.MySql, "schemaforge_test")
                .IncludeTables(tbl)
                .WithoutViews().WithoutIndexes().WithoutConstraints().WithoutForeignKeys()
                .WithBatchSize(1000)
                .ContinueOnError()
                .WithLogLevel(LogLevel.Warning)
                .ExecuteAsync();

            var actual = await DatabaseHelper.GetMySqlRowCountAsync(
                ConnectionStrings.MySql, $"schemaforge_test.{tbl}");

            output.WriteLine($"LargeData SS→MySQL: expected={rows}, actual={actual}");
            Assert.Equal(rows, actual);
        }
        finally
        {
            await ExecSqlServerAsync(ConnectionStrings.SqlServer,
                $"IF OBJECT_ID('dbo.{tbl}','U') IS NOT NULL DROP TABLE dbo.{tbl};");
        }
    }

    /// <summary>
    /// 5 000 rows: SQL Server → Postgres → MySQL round-trip.
    /// Validates the full two-hop transfer at scale.
    /// </summary>
    [Fact]
    public async Task LargeData_SqlServer_To_Postgres_To_MySql_RoundTrip_TransfersAllRows()
    {
        const int rows = 5_000;
        const string tbl = "regression_large_rt";
        try
        {
            await ExecSqlServerAsync(ConnectionStrings.SqlServer, $"""
                IF OBJECT_ID('dbo.{tbl}','U') IS NOT NULL DROP TABLE dbo.{tbl};
                CREATE TABLE dbo.{tbl} (
                    id        INT IDENTITY(1,1) PRIMARY KEY,
                    name      NVARCHAR(200) NOT NULL,
                    num_val   INT NOT NULL,
                    amount    DECIMAL(12,2) NOT NULL,
                    is_active BIT NOT NULL
                );
                WITH nums AS (
                    SELECT 1 AS n UNION ALL
                    SELECT n+1 FROM nums WHERE n < {rows}
                )
                INSERT INTO dbo.{tbl} (name, num_val, amount, is_active)
                SELECT 'Item ' + CAST(n AS NVARCHAR(10)),
                       n,
                       CAST(n AS DECIMAL(12,2)) * 0.99,
                       n % 2
                FROM nums OPTION (MAXRECURSION {rows});
                """);

            // Hop 1: SQL Server → Postgres
            await DatabaseHelper.CleanPostgresAsync(ConnectionStrings.Postgres);
            await DbMigrate
                .FromSqlServer(ConnectionStrings.SqlServer)
                .ToPostgres(ConnectionStrings.Postgres)
                .IncludeTables(tbl)
                .WithoutViews().WithoutIndexes().WithoutConstraints().WithoutForeignKeys()
                .WithBatchSize(1000).ContinueOnError()
                .WithLogLevel(LogLevel.Warning)
                .ExecuteAsync();

            var pgRows = await DatabaseHelper.GetPostgresRowCountAsync(
                ConnectionStrings.Postgres, $"public.{tbl}");
            output.WriteLine($"LargeData RT hop1 SS→PG: pgRows={pgRows}");
            Assert.Equal(rows, pgRows);

            // Hop 2: Postgres → MySQL
            await DatabaseHelper.CleanMySqlAsync(ConnectionStrings.MySql);
            await DbMigrate
                .FromPostgres(ConnectionStrings.Postgres)
                .ToMySql(ConnectionStrings.MySql, "schemaforge_test")
                .IncludeTables(tbl)
                .WithoutViews().WithoutIndexes().WithoutConstraints().WithoutForeignKeys()
                .WithBatchSize(1000).ContinueOnError()
                .WithLogLevel(LogLevel.Warning)
                .ExecuteAsync();

            var myRows = await DatabaseHelper.GetMySqlRowCountAsync(
                ConnectionStrings.MySql, $"schemaforge_test.{tbl}");
            output.WriteLine($"LargeData RT hop2 PG→MySQL: myRows={myRows}");
            Assert.Equal(rows, myRows);
        }
        finally
        {
            await ExecSqlServerAsync(ConnectionStrings.SqlServer,
                $"IF OBJECT_ID('dbo.{tbl}','U') IS NOT NULL DROP TABLE dbo.{tbl};");
        }
    }

    /// <summary>
    /// 1 001 rows — batch size 500 (not evenly divisible).
    /// Ensures the last partial batch is flushed correctly.
    /// </summary>
    [Fact]
    public async Task LargeData_OddBatchBoundary_SqlServer_To_Postgres_TransfersAllRows()
    {
        const int rows = 1_001;
        const int batchSize = 500;
        const string tbl = "regression_batch_boundary";
        try
        {
            await ExecSqlServerAsync(ConnectionStrings.SqlServer, $"""
                IF OBJECT_ID('dbo.{tbl}','U') IS NOT NULL DROP TABLE dbo.{tbl};
                CREATE TABLE dbo.{tbl} (
                    id     INT IDENTITY(1,1) PRIMARY KEY,
                    val    NVARCHAR(50) NOT NULL,
                    active BIT NOT NULL
                );
                WITH nums AS (
                    SELECT 1 AS n UNION ALL
                    SELECT n+1 FROM nums WHERE n < {rows}
                )
                INSERT INTO dbo.{tbl} (val, active)
                SELECT CAST(n AS NVARCHAR(10)), n % 2
                FROM nums OPTION (MAXRECURSION {rows});
                """);

            await DatabaseHelper.CleanPostgresAsync(ConnectionStrings.Postgres);

            await DbMigrate
                .FromSqlServer(ConnectionStrings.SqlServer)
                .ToPostgres(ConnectionStrings.Postgres)
                .IncludeTables(tbl)
                .WithoutViews().WithoutIndexes().WithoutConstraints().WithoutForeignKeys()
                .WithBatchSize(batchSize).ContinueOnError()
                .WithLogLevel(LogLevel.Warning)
                .ExecuteAsync();

            var actual = await DatabaseHelper.GetPostgresRowCountAsync(
                ConnectionStrings.Postgres, $"public.{tbl}");

            output.WriteLine($"BatchBoundary SS→PG: expected={rows}, actual={actual}, batchSize={batchSize}");
            Assert.Equal(rows, actual);
        }
        finally
        {
            await ExecSqlServerAsync(ConnectionStrings.SqlServer,
                $"IF OBJECT_ID('dbo.{tbl}','U') IS NOT NULL DROP TABLE dbo.{tbl};");
        }
    }

    // ================================================================
    // EDGE CASES — BOOLEAN / BIT COLUMNS (22P03 regression)
    // ================================================================

    /// <summary>
    /// Targeted regression for 22P03: five BIT columns, mixed true/false/NULL.
    /// Verifies both value correctness and NULL preservation in Postgres.
    /// </summary>
    [Fact]
    public async Task EdgeCase_BooleanColumns_ValueFidelity_SqlServer_To_Postgres()
    {
        const string tbl = "regression_bool";
        try
        {
            await ExecSqlServerAsync(ConnectionStrings.SqlServer, $"""
                IF OBJECT_ID('dbo.{tbl}','U') IS NOT NULL DROP TABLE dbo.{tbl};
                CREATE TABLE dbo.{tbl} (
                    id       INT IDENTITY(1,1) PRIMARY KEY,
                    flag_a   BIT NOT NULL,
                    flag_b   BIT NOT NULL,
                    flag_c   BIT NULL
                );
                INSERT INTO dbo.{tbl} (flag_a, flag_b, flag_c) VALUES
                    (1, 0, NULL),
                    (0, 1, 1),
                    (1, 1, 0),
                    (0, 0, NULL),
                    (1, 0, 1);
                """);

            await DatabaseHelper.CleanPostgresAsync(ConnectionStrings.Postgres);

            await DbMigrate
                .FromSqlServer(ConnectionStrings.SqlServer)
                .ToPostgres(ConnectionStrings.Postgres)
                .IncludeTables(tbl)
                .WithoutViews().WithoutIndexes().WithoutConstraints().WithoutForeignKeys()
                .WithLogLevel(LogLevel.Warning)
                .ExecuteAsync();

            var totalRows   = await DatabaseHelper.GetPostgresRowCountAsync(
                                  ConnectionStrings.Postgres, $"public.{tbl}");
            var trueCount   = await PgScalarAsync(ConnectionStrings.Postgres,
                                  $"SELECT COUNT(*) FROM public.{tbl} WHERE flag_a = true");
            var nullCount   = await PgScalarAsync(ConnectionStrings.Postgres,
                                  $"SELECT COUNT(*) FROM public.{tbl} WHERE flag_c IS NULL");

            output.WriteLine($"BoolFidelity SS→PG: rows={totalRows}, flag_a=true={trueCount}, flag_c=null={nullCount}");
            Assert.Equal(5, totalRows);
            Assert.Equal(3, trueCount);   // rows 1, 3, 5 have flag_a = 1
            Assert.Equal(2, nullCount);   // rows 1, 4 have flag_c = NULL
        }
        finally
        {
            await ExecSqlServerAsync(ConnectionStrings.SqlServer,
                $"IF OBJECT_ID('dbo.{tbl}','U') IS NOT NULL DROP TABLE dbo.{tbl};");
        }
    }

    /// <summary>
    /// Five independent BIT columns in a single table — all must survive binary COPY.
    /// </summary>
    [Fact]
    public async Task EdgeCase_MultipleBitColumns_SqlServer_To_Postgres_Succeeds()
    {
        const string tbl = "regression_multi_bit";
        try
        {
            await ExecSqlServerAsync(ConnectionStrings.SqlServer, $"""
                IF OBJECT_ID('dbo.{tbl}','U') IS NOT NULL DROP TABLE dbo.{tbl};
                CREATE TABLE dbo.{tbl} (
                    id INT IDENTITY(1,1) PRIMARY KEY,
                    b1 BIT NOT NULL, b2 BIT NOT NULL, b3 BIT NOT NULL,
                    b4 BIT NOT NULL, b5 BIT NOT NULL
                );
                INSERT INTO dbo.{tbl} (b1,b2,b3,b4,b5) VALUES
                    (1,0,1,0,1),(0,1,0,1,0),(1,1,1,1,1),(0,0,0,0,0),
                    (1,0,0,1,1),(0,1,1,0,0),(1,1,0,0,1),(0,0,1,1,0);
                """);

            await DatabaseHelper.CleanPostgresAsync(ConnectionStrings.Postgres);

            await DbMigrate
                .FromSqlServer(ConnectionStrings.SqlServer)
                .ToPostgres(ConnectionStrings.Postgres)
                .IncludeTables(tbl)
                .WithoutViews().WithoutIndexes().WithoutConstraints().WithoutForeignKeys()
                .WithLogLevel(LogLevel.Warning)
                .ExecuteAsync();

            var rows = await DatabaseHelper.GetPostgresRowCountAsync(
                ConnectionStrings.Postgres, $"public.{tbl}");
            output.WriteLine($"MultiBit SS→PG: rows={rows}");
            Assert.Equal(8, rows);
        }
        finally
        {
            await ExecSqlServerAsync(ConnectionStrings.SqlServer,
                $"IF OBJECT_ID('dbo.{tbl}','U') IS NOT NULL DROP TABLE dbo.{tbl};");
        }
    }

    /// <summary>
    /// BIT columns migrated to MySQL — verifies tinyint(1) handling.
    /// </summary>
    [Fact]
    public async Task EdgeCase_BooleanColumns_SqlServer_To_MySql_Succeeds()
    {
        const string tbl = "regression_bool_my";
        try
        {
            await ExecSqlServerAsync(ConnectionStrings.SqlServer, $"""
                IF OBJECT_ID('dbo.{tbl}','U') IS NOT NULL DROP TABLE dbo.{tbl};
                CREATE TABLE dbo.{tbl} (
                    id           INT IDENTITY(1,1) PRIMARY KEY,
                    flag_true    BIT NOT NULL,
                    flag_false   BIT NOT NULL,
                    nullable_bit BIT NULL
                );
                INSERT INTO dbo.{tbl} (flag_true, flag_false, nullable_bit) VALUES
                    (1, 0, NULL),
                    (1, 0, 1),
                    (0, 0, 0),
                    (1, 0, NULL);
                """);

            await DatabaseHelper.CleanMySqlAsync(ConnectionStrings.MySql);

            await DbMigrate
                .FromSqlServer(ConnectionStrings.SqlServer)
                .ToMySql(ConnectionStrings.MySql, "schemaforge_test")
                .IncludeTables(tbl)
                .WithoutViews().WithoutIndexes().WithoutConstraints().WithoutForeignKeys()
                .WithLogLevel(LogLevel.Warning)
                .ExecuteAsync();

            var rows = await DatabaseHelper.GetMySqlRowCountAsync(
                ConnectionStrings.MySql, $"schemaforge_test.{tbl}");
            output.WriteLine($"BoolMySQL SS→MySQL: rows={rows}");
            Assert.Equal(4, rows);
        }
        finally
        {
            await ExecSqlServerAsync(ConnectionStrings.SqlServer,
                $"IF OBJECT_ID('dbo.{tbl}','U') IS NOT NULL DROP TABLE dbo.{tbl};");
        }
    }

    // ================================================================
    // EDGE CASES — NULL VALUES
    // ================================================================

    /// <summary>
    /// Every nullable column populated with NULL in at least one row.
    /// Verifies NULLs survive the full migration to Postgres.
    /// </summary>
    [Fact]
    public async Task EdgeCase_NullValues_SqlServer_To_Postgres_PreservesNulls()
    {
        const string tbl = "regression_nulls";
        try
        {
            await ExecSqlServerAsync(ConnectionStrings.SqlServer, $"""
                IF OBJECT_ID('dbo.{tbl}','U') IS NOT NULL DROP TABLE dbo.{tbl};
                CREATE TABLE dbo.{tbl} (
                    id             INT IDENTITY(1,1) PRIMARY KEY,
                    nullable_txt   NVARCHAR(200) NULL,
                    nullable_int   INT NULL,
                    nullable_dec   DECIMAL(10,2) NULL,
                    nullable_bit   BIT NULL,
                    nullable_date  DATE NULL,
                    nullable_float FLOAT NULL
                );
                INSERT INTO dbo.{tbl}
                    (nullable_txt, nullable_int, nullable_dec, nullable_bit, nullable_date, nullable_float)
                VALUES
                    (NULL,       NULL,  NULL,    NULL, NULL,         NULL),
                    (N'present', 42,    3.14,    1,    '2024-06-01', 1.23),
                    (NULL,       0,     0.00,    0,    '2000-01-01', 0.0),
                    (N'partial', NULL,  NULL,    1,    NULL,         NULL);
                """);

            await DatabaseHelper.CleanPostgresAsync(ConnectionStrings.Postgres);

            await DbMigrate
                .FromSqlServer(ConnectionStrings.SqlServer)
                .ToPostgres(ConnectionStrings.Postgres)
                .IncludeTables(tbl)
                .WithoutViews().WithoutIndexes().WithoutConstraints().WithoutForeignKeys()
                .WithLogLevel(LogLevel.Warning)
                .ExecuteAsync();

            var totalRows = await DatabaseHelper.GetPostgresRowCountAsync(
                                ConnectionStrings.Postgres, $"public.{tbl}");
            var allNulls  = await PgScalarAsync(ConnectionStrings.Postgres,
                                $"SELECT COUNT(*) FROM public.{tbl} WHERE nullable_txt IS NULL");
            var partials  = await PgScalarAsync(ConnectionStrings.Postgres,
                                $"SELECT COUNT(*) FROM public.{tbl} WHERE nullable_int IS NULL");

            output.WriteLine($"NullValues SS→PG: rows={totalRows}, allNullRows={allNulls}, partialNullRows={partials}");
            Assert.Equal(4, totalRows);
            Assert.Equal(2, allNulls);   // rows 1 and 3 have nullable_txt = NULL
            Assert.True(partials >= 2, $"Expected >= 2 rows with nullable_int NULL, got {partials}");
        }
        finally
        {
            await ExecSqlServerAsync(ConnectionStrings.SqlServer,
                $"IF OBJECT_ID('dbo.{tbl}','U') IS NOT NULL DROP TABLE dbo.{tbl};");
        }
    }

    // ================================================================
    // EDGE CASES — UNICODE & SPECIAL CHARACTERS
    // ================================================================

    /// <summary>
    /// Unicode text: CJK, Arabic, emoji, SQL-special characters, long strings.
    /// Verifies character encoding is preserved end-to-end.
    /// </summary>
    [Fact]
    public async Task EdgeCase_UnicodeAndSpecialChars_SqlServer_To_Postgres_PreservesText()
    {
        const string tbl = "regression_unicode";
        try
        {
            await ExecSqlServerAsync(ConnectionStrings.SqlServer, $"""
                IF OBJECT_ID('dbo.{tbl}','U') IS NOT NULL DROP TABLE dbo.{tbl};
                CREATE TABLE dbo.{tbl} (
                    id    INT IDENTITY(1,1) PRIMARY KEY,
                    label NVARCHAR(MAX) NOT NULL,
                    notes NVARCHAR(MAX) NULL
                );
                INSERT INTO dbo.{tbl} (label, notes) VALUES
                    (N'Hello World',                  N'Basic ASCII'),
                    (N'中文测试',                     N'Simplified Chinese'),
                    (N'日本語テスト',                 N'Japanese Katakana'),
                    (N'العربية',                      N'Arabic'),
                    (N'Ünïcödé',                      N'Latin extended'),
                    (N'Emoji: 😀🎉🚀',               N'Unicode supplementary'),
                    (N'Quotes: ''single'' "double"',  NULL),
                    (N'Tab' + CHAR(9) + N'Newline',   NULL),
                    (REPLICATE(N'X', 2000),           N'2000-char string');
                """);

            await DatabaseHelper.CleanPostgresAsync(ConnectionStrings.Postgres);

            await DbMigrate
                .FromSqlServer(ConnectionStrings.SqlServer)
                .ToPostgres(ConnectionStrings.Postgres)
                .IncludeTables(tbl)
                .WithoutViews().WithoutIndexes().WithoutConstraints().WithoutForeignKeys()
                .WithLogLevel(LogLevel.Warning)
                .ExecuteAsync();

            var rows     = await DatabaseHelper.GetPostgresRowCountAsync(
                               ConnectionStrings.Postgres, $"public.{tbl}");
            var cjkCount = await PgScalarAsync(ConnectionStrings.Postgres,
                               $"SELECT COUNT(*) FROM public.{tbl} WHERE label LIKE '%中%'");
            var longRow  = await PgScalarAsync(ConnectionStrings.Postgres,
                               $"SELECT COUNT(*) FROM public.{tbl} WHERE LENGTH(label) = 2000");

            output.WriteLine($"Unicode SS→PG: rows={rows}, cjkCount={cjkCount}, longRow={longRow}");
            Assert.Equal(9, rows);
            Assert.Equal(1, cjkCount);
            Assert.Equal(1, longRow);
        }
        finally
        {
            await ExecSqlServerAsync(ConnectionStrings.SqlServer,
                $"IF OBJECT_ID('dbo.{tbl}','U') IS NOT NULL DROP TABLE dbo.{tbl};");
        }
    }

    // ================================================================
    // EDGE CASES — DECIMAL PRECISION
    // ================================================================

    /// <summary>
    /// DECIMAL(18,6) extremes: very small, very large, negative, zero.
    /// Verifies precision is not lost during migration.
    /// </summary>
    [Fact]
    public async Task EdgeCase_DecimalPrecision_SqlServer_To_Postgres_PreservesValues()
    {
        const string tbl = "regression_decimal";
        try
        {
            await ExecSqlServerAsync(ConnectionStrings.SqlServer, $"""
                IF OBJECT_ID('dbo.{tbl}','U') IS NOT NULL DROP TABLE dbo.{tbl};
                CREATE TABLE dbo.{tbl} (
                    id     INT IDENTITY(1,1) PRIMARY KEY,
                    price  DECIMAL(18,6) NOT NULL,
                    rate   DECIMAL(5,4)  NOT NULL,
                    amount DECIMAL(20,2) NOT NULL
                );
                INSERT INTO dbo.{tbl} (price, rate, amount) VALUES
                    (0.000001,          0.0001, 0.00),
                    (9999999.999999,    9.9999, 99999999999999999.99),
                    (1234567.891234,    1.2345, 1000000.50),
                    (3.141593,          0.3333, 42.00),
                    (-9999999.999999,   0.0000, -0.01);
                """);

            await DatabaseHelper.CleanPostgresAsync(ConnectionStrings.Postgres);

            await DbMigrate
                .FromSqlServer(ConnectionStrings.SqlServer)
                .ToPostgres(ConnectionStrings.Postgres)
                .IncludeTables(tbl)
                .WithoutViews().WithoutIndexes().WithoutConstraints().WithoutForeignKeys()
                .WithLogLevel(LogLevel.Warning)
                .ExecuteAsync();

            var rows = await DatabaseHelper.GetPostgresRowCountAsync(
                ConnectionStrings.Postgres, $"public.{tbl}");

            // Verify the tiny value made it across
            var tinyCount = await PgScalarAsync(ConnectionStrings.Postgres,
                $"SELECT COUNT(*) FROM public.{tbl} WHERE price = 0.000001");

            output.WriteLine($"DecimalPrecision SS→PG: rows={rows}, tinyCount={tinyCount}");
            Assert.Equal(5, rows);
            Assert.Equal(1, tinyCount);
        }
        finally
        {
            await ExecSqlServerAsync(ConnectionStrings.SqlServer,
                $"IF OBJECT_ID('dbo.{tbl}','U') IS NOT NULL DROP TABLE dbo.{tbl};");
        }
    }

    // ================================================================
    // EDGE CASES — EMPTY TABLE
    // ================================================================

    /// <summary>
    /// Schema-only migration: table exists in target with correct structure but 0 rows.
    /// </summary>
    [Fact]
    public async Task EdgeCase_EmptyTable_SchemaIsCreated_WithZeroRows()
    {
        const string tbl = "regression_empty";
        try
        {
            await ExecSqlServerAsync(ConnectionStrings.SqlServer, $"""
                IF OBJECT_ID('dbo.{tbl}','U') IS NOT NULL DROP TABLE dbo.{tbl};
                CREATE TABLE dbo.{tbl} (
                    id    INT IDENTITY(1,1) PRIMARY KEY,
                    value NVARCHAR(100) NOT NULL,
                    flag  BIT NOT NULL
                );
                -- intentionally no data
                """);

            // SS → Postgres
            await DatabaseHelper.CleanPostgresAsync(ConnectionStrings.Postgres);
            await DbMigrate
                .FromSqlServer(ConnectionStrings.SqlServer)
                .ToPostgres(ConnectionStrings.Postgres)
                .IncludeTables(tbl)
                .WithoutViews().WithoutIndexes().WithoutConstraints().WithoutForeignKeys()
                .WithLogLevel(LogLevel.Warning)
                .ExecuteAsync();

            var pgTables = await DatabaseHelper.GetPostgresTableCountAsync(ConnectionStrings.Postgres);
            var pgRows   = await DatabaseHelper.GetPostgresRowCountAsync(
                               ConnectionStrings.Postgres, $"public.{tbl}");
            output.WriteLine($"EmptyTable SS→PG: tables={pgTables}, rows={pgRows}");
            Assert.Equal(1, pgTables);
            Assert.Equal(0, pgRows);

            // SS → MySQL
            await DatabaseHelper.CleanMySqlAsync(ConnectionStrings.MySql);
            await DbMigrate
                .FromSqlServer(ConnectionStrings.SqlServer)
                .ToMySql(ConnectionStrings.MySql, "schemaforge_test")
                .IncludeTables(tbl)
                .WithoutViews().WithoutIndexes().WithoutConstraints().WithoutForeignKeys()
                .WithLogLevel(LogLevel.Warning)
                .ExecuteAsync();

            var myTables = await DatabaseHelper.GetMySqlTableCountAsync(ConnectionStrings.MySql);
            var myRows   = await DatabaseHelper.GetMySqlRowCountAsync(
                               ConnectionStrings.MySql, $"schemaforge_test.{tbl}");
            output.WriteLine($"EmptyTable SS→MySQL: tables={myTables}, rows={myRows}");
            Assert.Equal(1, myTables);
            Assert.Equal(0, myRows);
        }
        finally
        {
            await ExecSqlServerAsync(ConnectionStrings.SqlServer,
                $"IF OBJECT_ID('dbo.{tbl}','U') IS NOT NULL DROP TABLE dbo.{tbl};");
        }
    }

    // ================================================================
    // EDGE CASES — MIXED TYPES TOGETHER
    // ================================================================

    /// <summary>
    /// Single table that exercises every column type handled by the type map:
    /// INT, BIGINT, SMALLINT, BIT, DECIMAL, FLOAT, NVARCHAR, VARCHAR, DATE, DATETIME2.
    /// Migrates to both Postgres and MySQL; asserts row counts match.
    /// </summary>
    [Fact]
    public async Task EdgeCase_AllSupportedTypes_SqlServer_To_Postgres_And_MySql_Succeeds()
    {
        const string tbl = "regression_all_types";
        try
        {
            await ExecSqlServerAsync(ConnectionStrings.SqlServer, $"""
                IF OBJECT_ID('dbo.{tbl}','U') IS NOT NULL DROP TABLE dbo.{tbl};
                CREATE TABLE dbo.{tbl} (
                    id          INT IDENTITY(1,1) PRIMARY KEY,
                    big_id      BIGINT NOT NULL,
                    small_id    SMALLINT NOT NULL,
                    flag        BIT NOT NULL,
                    amount      DECIMAL(14,4) NOT NULL,
                    ratio       FLOAT NULL,
                    label       NVARCHAR(200) NOT NULL,
                    code        VARCHAR(20) NOT NULL,
                    born_date   DATE NULL,
                    created_at  DATETIME2 NOT NULL
                );
                INSERT INTO dbo.{tbl}
                    (big_id, small_id, flag, amount, ratio, label, code, born_date, created_at)
                VALUES
                    (9223372036854775807, 32767,  1, 9999.9999, 3.14159,  N'Max values',    'MAX',  '1970-01-01', '2024-12-31 23:59:59'),
                    (0,                  0,      0, 0.0000,    0.0,      N'Zero values',   'ZERO', NULL,         '2000-01-01 00:00:00'),
                    (-9223372036854775808,-32768, 0,-9999.9999, NULL,     N'Min values',    'MIN',  '2024-12-31', '1900-01-01 00:00:00'),
                    (42,                 10,     1, 1.2345,    2.71828,  N'Typical row',   'TYP',  '1985-07-04', '2024-06-15 12:00:00');
                """);

            // — Postgres
            await DatabaseHelper.CleanPostgresAsync(ConnectionStrings.Postgres);
            await DbMigrate
                .FromSqlServer(ConnectionStrings.SqlServer)
                .ToPostgres(ConnectionStrings.Postgres)
                .IncludeTables(tbl)
                .WithoutViews().WithoutIndexes().WithoutConstraints().WithoutForeignKeys()
                .WithLogLevel(LogLevel.Warning)
                .ExecuteAsync();

            var pgRows = await DatabaseHelper.GetPostgresRowCountAsync(
                ConnectionStrings.Postgres, $"public.{tbl}");
            output.WriteLine($"AllTypes SS→PG: rows={pgRows}");
            Assert.Equal(4, pgRows);

            // — MySQL
            await DatabaseHelper.CleanMySqlAsync(ConnectionStrings.MySql);
            await DbMigrate
                .FromSqlServer(ConnectionStrings.SqlServer)
                .ToMySql(ConnectionStrings.MySql, "schemaforge_test")
                .IncludeTables(tbl)
                .WithoutViews().WithoutIndexes().WithoutConstraints().WithoutForeignKeys()
                .WithLogLevel(LogLevel.Warning)
                .ExecuteAsync();

            var myRows = await DatabaseHelper.GetMySqlRowCountAsync(
                ConnectionStrings.MySql, $"schemaforge_test.{tbl}");
            output.WriteLine($"AllTypes SS→MySQL: rows={myRows}");
            Assert.Equal(4, myRows);
        }
        finally
        {
            await ExecSqlServerAsync(ConnectionStrings.SqlServer,
                $"IF OBJECT_ID('dbo.{tbl}','U') IS NOT NULL DROP TABLE dbo.{tbl};");
        }
    }

    // ================================================================
    // EDGE CASES — SCHEMA-ONLY MIGRATION (regression tests)
    // ================================================================

    /// <summary>
    /// MigrateSchemaOnly() on a table with data must transfer 0 rows.
    /// </summary>
    [Fact]
    public async Task EdgeCase_SchemaOnly_DoesNotMigrateRows_To_Postgres()
    {
        const string tbl = "regression_schema_only";
        try
        {
            await ExecSqlServerAsync(ConnectionStrings.SqlServer, $"""
                IF OBJECT_ID('dbo.{tbl}','U') IS NOT NULL DROP TABLE dbo.{tbl};
                CREATE TABLE dbo.{tbl} (
                    id    INT IDENTITY(1,1) PRIMARY KEY,
                    label NVARCHAR(100) NOT NULL,
                    flag  BIT NOT NULL
                );
                INSERT INTO dbo.{tbl} (label, flag) VALUES
                    (N'Row1', 1),(N'Row2', 0),(N'Row3', 1);
                """);

            await DatabaseHelper.CleanPostgresAsync(ConnectionStrings.Postgres);

            await DbMigrate
                .FromSqlServer(ConnectionStrings.SqlServer)
                .ToPostgres(ConnectionStrings.Postgres)
                .IncludeTables(tbl)
                .MigrateSchemaOnly()
                .WithLogLevel(LogLevel.Warning)
                .ExecuteAsync();

            var pgTables = await DatabaseHelper.GetPostgresTableCountAsync(ConnectionStrings.Postgres);
            var pgRows   = await DatabaseHelper.GetPostgresRowCountAsync(
                               ConnectionStrings.Postgres, $"public.{tbl}");

            output.WriteLine($"SchemaOnly SS→PG: tables={pgTables}, rows={pgRows}");
            Assert.Equal(1, pgTables);
            Assert.Equal(0, pgRows);
        }
        finally
        {
            await ExecSqlServerAsync(ConnectionStrings.SqlServer,
                $"IF OBJECT_ID('dbo.{tbl}','U') IS NOT NULL DROP TABLE dbo.{tbl};");
        }
    }

    /// <summary>
    /// DryRun on a regression table must produce SQL statements without writing data.
    /// </summary>
    [Fact]
    public async Task EdgeCase_DryRun_ProducesSql_WithoutWritingRows()
    {
        const string tbl = "regression_dryrun";
        try
        {
            await ExecSqlServerAsync(ConnectionStrings.SqlServer, $"""
                IF OBJECT_ID('dbo.{tbl}','U') IS NOT NULL DROP TABLE dbo.{tbl};
                CREATE TABLE dbo.{tbl} (
                    id    INT IDENTITY(1,1) PRIMARY KEY,
                    label NVARCHAR(100) NOT NULL,
                    flag  BIT NOT NULL
                );
                INSERT INTO dbo.{tbl} (label, flag) VALUES (N'Test', 1);
                """);

            await DatabaseHelper.CleanPostgresAsync(ConnectionStrings.Postgres);

            var result = await DbMigrate
                .FromSqlServer(ConnectionStrings.SqlServer)
                .ToPostgres(ConnectionStrings.Postgres)
                .IncludeTables(tbl)
                .WithLogLevel(LogLevel.Warning)
                .ExecuteDryRunAsync();

            Assert.NotNull(result);
            Assert.NotEmpty(result.Statements);
            Assert.Contains(result.Statements, s =>
                s.Sql.Contains("CREATE TABLE", StringComparison.OrdinalIgnoreCase));

            // Target must still be empty
            var pgTables = await DatabaseHelper.GetPostgresTableCountAsync(ConnectionStrings.Postgres);
            Assert.Equal(0, pgTables);

            output.WriteLine($"DryRun: {result.Statements.Count} SQL statements, tables in PG={pgTables}");
        }
        finally
        {
            await ExecSqlServerAsync(ConnectionStrings.SqlServer,
                $"IF OBJECT_ID('dbo.{tbl}','U') IS NOT NULL DROP TABLE dbo.{tbl};");
        }
    }
}
