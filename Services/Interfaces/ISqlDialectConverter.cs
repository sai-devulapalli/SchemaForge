namespace SchemaForge.Services.Interfaces;

/// <summary>
/// Interface for converting SQL expressions between different database dialects.
/// Handles function names, operators, and syntax differences between databases.
/// </summary>
public interface ISqlDialectConverter
{
    /// <summary>
    /// Converts a view's SQL definition from source to target database dialect.
    /// </summary>
    /// <param name="definition">Original view SQL definition.</param>
    /// <param name="sourceDb">Source database type.</param>
    /// <param name="targetDb">Target database type.</param>
    /// <returns>Converted SQL definition for target database.</returns>
    string ConvertViewDefinition(string definition, string sourceDb, string targetDb);

    /// <summary>
    /// Converts a CHECK constraint expression between database dialects.
    /// </summary>
    /// <param name="expression">Original check expression.</param>
    /// <param name="sourceDb">Source database type.</param>
    /// <param name="targetDb">Target database type.</param>
    /// <returns>Converted expression for target database.</returns>
    string ConvertCheckExpression(string expression, string sourceDb, string targetDb);

    /// <summary>
    /// Converts a DEFAULT value expression between database dialects.
    /// </summary>
    /// <param name="expression">Original default expression.</param>
    /// <param name="sourceDb">Source database type.</param>
    /// <param name="targetDb">Target database type.</param>
    /// <returns>Converted expression for target database.</returns>
    string ConvertDefaultExpression(string expression, string sourceDb, string targetDb);

    /// <summary>
    /// Converts a filtered index WHERE expression between database dialects.
    /// </summary>
    /// <param name="expression">Original filter expression.</param>
    /// <param name="sourceDb">Source database type.</param>
    /// <param name="targetDb">Target database type.</param>
    /// <returns>Converted expression for target database.</returns>
    string ConvertFilterExpression(string expression, string sourceDb, string targetDb);

    /// <summary>
    /// Detects the source database type based on SQL syntax patterns.
    /// </summary>
    /// <param name="sqlExpression">SQL expression to analyze.</param>
    /// <returns>Detected database type identifier.</returns>
    string DetectSourceDatabase(string sqlExpression);
}