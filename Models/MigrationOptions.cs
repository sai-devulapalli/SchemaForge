namespace SchemaForge.Models;

public class MigrationOptions
{
    /// <summary>
    /// Enable or disable schema migration (tables, columns, primary keys)
    /// </summary>
    public bool MigrateSchema { get; set; } = true;

    /// <summary>
    /// Enable or disable data migration
    /// </summary>
    public bool MigrateData { get; set; } = true;

    /// <summary>
    /// Enable or disable view migration
    /// </summary>
    public bool MigrateViews { get; set; } = true;

    /// <summary>
    /// Enable or disable index migration
    /// </summary>
    public bool MigrateIndexes { get; set; } = true;

    /// <summary>
    /// Enable or disable constraint migration (check, unique, default)
    /// </summary>
    public bool MigrateConstraints { get; set; } = true;

    /// <summary>
    /// Enable or disable foreign key migration
    /// </summary>
    public bool MigrateForeignKeys { get; set; } = true;

    /// <summary>
    /// Batch size for data migration (number of rows per batch)
    /// </summary>
    public int DataBatchSize { get; set; } = 1000;

    /// <summary>
    /// Continue migration even if some objects fail
    /// </summary>
    public bool ContinueOnError { get; set; } = true;

    /// <summary>
    /// Tables to include (if empty, all tables are included)
    /// </summary>
    public List<string> IncludeTables { get; set; } = [];

    /// <summary>
    /// Tables to exclude from migration
    /// </summary>
    public List<string> ExcludeTables { get; set; } = [];

    /// <summary>
    /// Dry run configuration. When DryRun.Enabled is true, SQL is generated but not executed.
    /// </summary>
    public DryRunOptions DryRun { get; set; } = new();

    /// <summary>
    /// Predefined configuration for schema-only migration
    /// </summary>
    public static MigrationOptions SchemaOnly => new()
    {
        MigrateSchema = true,
        MigrateData = false,
        MigrateViews = true,
        MigrateIndexes = true,
        MigrateConstraints = true,
        MigrateForeignKeys = true
    };

    /// <summary>
    /// Predefined configuration for full migration (schema + data)
    /// </summary>
    public static MigrationOptions Full => new()
    {
        MigrateSchema = true,
        MigrateData = true,
        MigrateViews = true,
        MigrateIndexes = true,
        MigrateConstraints = true,
        MigrateForeignKeys = true
    };

    /// <summary>
    /// Predefined configuration for data-only migration (assumes schema exists)
    /// </summary>
    public static MigrationOptions DataOnly => new()
    {
        MigrateSchema = false,
        MigrateData = true,
        MigrateViews = false,
        MigrateIndexes = false,
        MigrateConstraints = false,
        MigrateForeignKeys = false
    };

    /// <summary>
    /// Predefined configuration for tables-only migration (no views, indexes, constraints)
    /// </summary>
    public static MigrationOptions TablesOnly => new()
    {
        MigrateSchema = true,
        MigrateData = true,
        MigrateViews = false,
        MigrateIndexes = false,
        MigrateConstraints = false,
        MigrateForeignKeys = false
    };
}
