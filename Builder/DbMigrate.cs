namespace SchemaForge.Builder;

/// <summary>
/// Static entry point for the fluent migration API.
/// Provides a clean starting point for building migrations.
/// </summary>
public static class DbMigrate
{
    /// <summary>
    /// Start building a new migration from SQL Server.
    /// </summary>
    /// <param name="connectionString">SQL Server connection string.</param>
    /// <returns>Migration builder for further configuration.</returns>
    public static MigrationBuilder FromSqlServer(string connectionString)
    {
        return new MigrationBuilder().FromSqlServer(connectionString);
    }

    /// <summary>
    /// Start building a new migration from PostgreSQL.
    /// </summary>
    /// <param name="connectionString">PostgreSQL connection string.</param>
    /// <returns>Migration builder for further configuration.</returns>
    public static MigrationBuilder FromPostgres(string connectionString)
    {
        return new MigrationBuilder().FromPostgres(connectionString);
    }

    /// <summary>
    /// Start building a new migration from MySQL.
    /// </summary>
    /// <param name="connectionString">MySQL connection string.</param>
    /// <returns>Migration builder for further configuration.</returns>
    public static MigrationBuilder FromMySql(string connectionString)
    {
        return new MigrationBuilder().FromMySql(connectionString);
    }

    /// <summary>
    /// Start building a new migration from Oracle.
    /// </summary>
    /// <param name="connectionString">Oracle connection string.</param>
    /// <returns>Migration builder for further configuration.</returns>
    public static MigrationBuilder FromOracle(string connectionString)
    {
        return new MigrationBuilder().FromOracle(connectionString);
    }

    /// <summary>
    /// Start building a new migration from any supported database.
    /// </summary>
    /// <param name="databaseType">Database type: sqlserver, postgres, mysql, oracle.</param>
    /// <param name="connectionString">Connection string.</param>
    /// <returns>Migration builder for further configuration.</returns>
    public static MigrationBuilder From(string databaseType, string connectionString)
    {
        return new MigrationBuilder().From(databaseType, connectionString);
    }

    /// <summary>
    /// Create a new migration builder instance.
    /// </summary>
    /// <returns>New migration builder.</returns>
    public static MigrationBuilder Create()
    {
        return new MigrationBuilder();
    }
}
