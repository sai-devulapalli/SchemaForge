namespace SchemaForge.Abstractions.Interfaces;

/// <summary>
/// Interface for converting SQL expressions between different database dialects.
/// Handles function names, operators, and syntax differences between databases.
/// </summary>
public interface ISqlDialectConverter
{
    /// <summary>
    /// Converts a view's SQL definition from source to target database dialect.
    /// </summary>
    string ConvertViewDefinition(string definition, string sourceDb, string targetDb,
        string? sourceSchema = null, string? targetSchema = null,
        Dictionary<string, string>? tableNameMap = null);

    /// <summary>
    /// Converts a CHECK constraint expression between database dialects.
    /// </summary>
    string ConvertCheckExpression(string expression, string sourceDb, string targetDb);

    /// <summary>
    /// Converts a DEFAULT value expression between database dialects.
    /// </summary>
    string ConvertDefaultExpression(string expression, string sourceDb, string targetDb);

    /// <summary>
    /// Converts a filtered index WHERE expression between database dialects.
    /// </summary>
    string ConvertFilterExpression(string expression, string sourceDb, string targetDb);

    /// <summary>
    /// Detects the source database type based on SQL syntax patterns.
    /// </summary>
    string DetectSourceDatabase(string sqlExpression);
}