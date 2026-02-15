using SchemaForge.Abstractions.Models;

namespace SchemaForge.Abstractions.Interfaces;

/// <summary>
/// Collects SQL statements during migration for dry run mode.
/// </summary>
public interface ISqlCollector
{
    /// <summary>
    /// Whether collection is active (dry run mode).
    /// </summary>
    bool IsCollecting { get; }

    /// <summary>
    /// Add a SQL statement to the collection.
    /// </summary>
    void AddSql(string sql, string category, string? objectName = null);

    /// <summary>
    /// Add a section header/comment.
    /// </summary>
    void AddComment(string comment);

    /// <summary>
    /// Get all collected SQL statements.
    /// </summary>
    IReadOnlyList<SqlStatement> GetStatements();

    /// <summary>
    /// Get complete SQL script as a single string.
    /// </summary>
    string GetScript();

    /// <summary>
    /// Clear all collected statements.
    /// </summary>
    void Clear();
}