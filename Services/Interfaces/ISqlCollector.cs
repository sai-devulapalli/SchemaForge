using SchemaForge.Models;

namespace SchemaForge.Services.Interfaces;

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
    /// <param name="sql">The SQL statement.</param>
    /// <param name="category">Category (Tables, Indexes, Constraints, Views, ForeignKeys, Data).</param>
    /// <param name="objectName">Optional name of the database object.</param>
    void AddSql(string sql, string category, string? objectName = null);

    /// <summary>
    /// Add a section header/comment.
    /// </summary>
    /// <param name="comment">Comment text.</param>
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