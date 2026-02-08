namespace SchemaForge.Models;

/// <summary>
/// Configuration options for dry run mode.
/// </summary>
public class DryRunOptions
{
    /// <summary>
    /// When true, SQL is generated but not executed.
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// Output file path for generated SQL. If null, outputs to console only.
    /// </summary>
    public string? OutputFilePath { get; set; }

    /// <summary>
    /// Include sample INSERT statements for data preview.
    /// </summary>
    public bool IncludeDataSamples { get; set; } = true;

    /// <summary>
    /// Number of sample INSERT statements per table when IncludeDataSamples is true.
    /// </summary>
    public int SampleRowCount { get; set; } = 5;

    /// <summary>
    /// Add comments/headers for each migration step.
    /// </summary>
    public bool IncludeComments { get; set; } = true;
}