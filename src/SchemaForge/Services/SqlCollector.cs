using System.Text;
using SchemaForge.Abstractions.Interfaces;
using SchemaForge.Abstractions.Models;

namespace SchemaForge.Services;

/// <summary>
/// Default implementation of ISqlCollector.
/// Collects SQL statements for dry run output.
/// </summary>
public class SqlCollector : ISqlCollector
{
    private readonly List<SqlStatement> _statements = [];
    private readonly bool _isCollecting;
    private readonly bool _includeComments;

    public SqlCollector(bool isCollecting, bool includeComments = true)
    {
        _isCollecting = isCollecting;
        _includeComments = includeComments;
    }

    public bool IsCollecting => _isCollecting;

    public void AddSql(string sql, string category, string? objectName = null)
    {
        if (!_isCollecting) return;
        _statements.Add(new SqlStatement(sql.Trim(), category, objectName, DateTime.UtcNow));
    }

    public void AddComment(string comment)
    {
        if (!_isCollecting || !_includeComments) return;
        _statements.Add(new SqlStatement($"-- {comment}", "Comment", null, DateTime.UtcNow));
    }

    public IReadOnlyList<SqlStatement> GetStatements() => _statements.AsReadOnly();

    public string GetScript()
    {
        var sb = new StringBuilder();
        sb.AppendLine("-- ============================================");
        sb.AppendLine("-- SchemaForge Dry Run SQL Script");
        sb.AppendLine($"-- Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        sb.AppendLine("-- ============================================");
        sb.AppendLine();

        string? currentCategory = null;
        foreach (var stmt in _statements)
        {
            if (stmt.Category != "Comment" && stmt.Category != currentCategory)
            {
                currentCategory = stmt.Category;
                sb.AppendLine();
                sb.AppendLine($"-- === {currentCategory} ===");
            }

            sb.AppendLine(stmt.Sql);
            if (!stmt.Sql.TrimEnd().EndsWith(';'))
                sb.AppendLine(";");
            sb.AppendLine();
        }

        return sb.ToString();
    }

    public void Clear() => _statements.Clear();
}