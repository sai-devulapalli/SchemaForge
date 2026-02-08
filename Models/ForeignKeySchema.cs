namespace SchemaForge.Models;

/// <summary>
/// Represents a foreign key relationship between tables.
/// Defines the referential integrity constraint from a child table to a parent table.
/// </summary>
public record ForeignKeySchema
{
    /// <summary>The name of the foreign key constraint.</summary>
    public required string Name { get; init; }

    /// <summary>The column in the child table that holds the foreign key.</summary>
    public required string ColumnName { get; init; }

    /// <summary>The schema of the referenced (parent) table.</summary>
    public required string ReferencedSchema { get; init; }

    /// <summary>The name of the referenced (parent) table.</summary>
    public required string ReferencedTable { get; init; }

    /// <summary>The column in the parent table that is referenced (usually the primary key).</summary>
    public required string ReferencedColumn { get; init; }
}