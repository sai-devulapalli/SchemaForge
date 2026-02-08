namespace SchemaForge.Models;

/// <summary>
/// Represents the schema definition of a database column.
/// Contains all metadata needed to recreate the column in a target database.
/// </summary>
public record ColumnSchema
{
    /// <summary>The name of the column in the source database.</summary>
    public required string ColumnName { get; init; }

    /// <summary>The data type of the column (e.g., "int", "varchar", "datetime").</summary>
    public required string DataType { get; init; }

    /// <summary>Maximum length for string/binary types. -1 indicates MAX (unlimited).</summary>
    public int? MaxLength { get; init; }

    /// <summary>Numeric precision for decimal/numeric types.</summary>
    public byte? Precision { get; init; }

    /// <summary>Numeric scale (decimal places) for decimal/numeric types.</summary>
    public int? Scale { get; init; }

    /// <summary>Whether the column allows NULL values.</summary>
    public bool IsNullable { get; init; }

    /// <summary>The default value expression for the column, if any.</summary>
    public string? DefaultValue { get; init; }

    /// <summary>Whether the column is an identity/auto-increment column.</summary>
    public bool IsIdentity { get; init; }
}
