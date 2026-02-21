using Microsoft.Data.SqlClient;
using MySql.Data.MySqlClient;
using Npgsql;

namespace SchemaForge.PackageTests;

/// <summary>
/// Helpers for seeding, cleaning, and querying databases in package tests.
/// </summary>
internal static class DatabaseHelper
{
    // ----------------------------------------------------------------
    // Row count queries
    // ----------------------------------------------------------------

    public static async Task<int> GetSqlServerTableCountAsync(string conn, string schema = "dbo")
    {
        await using var c = new SqlConnection(conn);
        await c.OpenAsync();
        await using var cmd = c.CreateCommand();
        cmd.CommandText = $"SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_TYPE='BASE TABLE' AND TABLE_SCHEMA='{schema}'";
        return (int)(await cmd.ExecuteScalarAsync() ?? 0);
    }

    public static async Task<int> GetSqlServerTotalRowsAsync(string conn, string schema = "dbo")
    {
        await using var c = new SqlConnection(conn);
        await c.OpenAsync();
        await using var cmd = c.CreateCommand();
        cmd.CommandText = $"SELECT ISNULL(SUM(p.rows),0) FROM sys.partitions p INNER JOIN sys.tables t ON p.object_id=t.object_id WHERE p.index_id<2 AND SCHEMA_NAME(t.schema_id)='{schema}'";
        return Convert.ToInt32(await cmd.ExecuteScalarAsync() ?? 0);
    }

    public static async Task<int> GetPostgresTableCountAsync(string conn)
    {
        await using var c = new NpgsqlConnection(conn);
        await c.OpenAsync();
        await using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema='public' AND table_type='BASE TABLE'";
        return Convert.ToInt32(await cmd.ExecuteScalarAsync() ?? 0);
    }

    public static async Task<int> GetPostgresRowCountAsync(string conn, string table)
    {
        await using var c = new NpgsqlConnection(conn);
        await c.OpenAsync();
        await using var cmd = c.CreateCommand();
        cmd.CommandText = $"SELECT COUNT(*) FROM {table}";
        return Convert.ToInt32(await cmd.ExecuteScalarAsync() ?? 0);
    }

    public static async Task<int> GetMySqlTableCountAsync(string conn)
    {
        await using var c = new MySqlConnection(conn);
        await c.OpenAsync();
        await using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema='schemaforge_test' AND table_type='BASE TABLE'";
        return Convert.ToInt32(await cmd.ExecuteScalarAsync() ?? 0);
    }

    public static async Task<int> GetMySqlRowCountAsync(string conn, string table)
    {
        await using var c = new MySqlConnection(conn);
        await c.OpenAsync();
        await using var cmd = c.CreateCommand();
        cmd.CommandText = $"SELECT COUNT(*) FROM {table}";
        return Convert.ToInt32(await cmd.ExecuteScalarAsync() ?? 0);
    }

    // ----------------------------------------------------------------
    // Cleanup helpers
    // ----------------------------------------------------------------

    public static async Task CleanPostgresAsync(string conn)
    {
        await using var c = new NpgsqlConnection(conn);
        await c.OpenAsync();
        await using var cmd = c.CreateCommand();
        cmd.CommandText = "DROP SCHEMA IF EXISTS public CASCADE; CREATE SCHEMA public;";
        await cmd.ExecuteNonQueryAsync();
    }

    public static async Task CleanMySqlAsync(string conn)
    {
        await using var c = new MySqlConnection(conn);
        await c.OpenAsync();
        await using var cmd = c.CreateCommand();
        cmd.CommandText = "DROP DATABASE IF EXISTS schemaforge_test; CREATE DATABASE schemaforge_test;";
        await cmd.ExecuteNonQueryAsync();
    }

    public static async Task CleanSqlServerSchemaAsync(string conn, string schema = "dbo")
    {
        var drop = @"
IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = @schema)
    EXEC('CREATE SCHEMA [' + @schema + ']');
DECLARE @sql NVARCHAR(MAX) = '';
SELECT @sql += 'ALTER TABLE ' + QUOTENAME(OBJECT_SCHEMA_NAME(parent_object_id)) + '.' + QUOTENAME(OBJECT_NAME(parent_object_id)) + ' DROP CONSTRAINT ' + QUOTENAME(name) + ';' + CHAR(10)
FROM sys.foreign_keys WHERE OBJECT_SCHEMA_NAME(parent_object_id) = @schema;
EXEC sp_executesql @sql;
SET @sql = '';
SELECT @sql += 'DROP TABLE ' + QUOTENAME(TABLE_SCHEMA) + '.' + QUOTENAME(TABLE_NAME) + ';' + CHAR(10)
FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_TYPE='BASE TABLE' AND TABLE_SCHEMA=@schema;
EXEC sp_executesql @sql;";
        await using var c = new SqlConnection(conn);
        await c.OpenAsync();
        await using var cmd = c.CreateCommand();
        cmd.CommandText = drop;
        cmd.Parameters.AddWithValue("@schema", schema);
        await cmd.ExecuteNonQueryAsync();
    }
}
