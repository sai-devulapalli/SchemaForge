namespace SchemaForge.Abstractions.Models;

/// <summary>
/// Represents the complete schema definition of a database table.
/// Contains all structural information needed to recreate the table in a target database.
/// </summary>
public record TableSchema
{
    /// <summary>The schema/owner name (e.g., "dbo" in SQL Server, "public" in PostgreSQL).</summary>
    public required string SchemaName { get; init; }

    /// <summary>The name of the table.</summary>
    public required string TableName { get; init; }

    /// <summary>List of all columns in the table with their definitions.</summary>
    public List<ColumnSchema> Columns { get; init; } = [];

    /// <summary>List of column names that make up the primary key.</summary>
    public List<string> PrimaryKeys { get; init; } = [];

    /// <summary>List of foreign key relationships to other tables.</summary>
    public List<ForeignKeySchema> ForeignKeys { get; init; } = [];

    /// <summary>List of indexes defined on the table.</summary>
    public List<IndexSchema> Indexes { get; init; } = [];

    /// <summary>List of constraints (check, unique, default) on the table.</summary>
    public List<ConstraintSchema> Constraints { get; init; } = [];
}