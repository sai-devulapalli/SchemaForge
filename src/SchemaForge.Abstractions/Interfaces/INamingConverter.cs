namespace SchemaForge.Abstractions.Interfaces;

/// <summary>
/// Interface for converting identifier names between different naming conventions.
/// Handles conversion of table names, column names, etc. to match target database standards.
/// </summary>
public interface INamingConverter
{
    /// <summary>
    /// Converts an identifier name to the target naming convention.
    /// </summary>
    /// <param name="input">The original identifier name from source database.</param>
    /// <returns>The converted identifier name for target database.</returns>
    string Convert(string input);
}