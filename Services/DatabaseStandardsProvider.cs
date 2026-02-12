using SchemaForge.Models;
using SchemaForge.Services.Interfaces;

namespace SchemaForge.Services;

/// <summary>
/// Provides database-specific standards and conventions.
/// Contains naming conventions, reserved keywords, identifier rules for each supported database.
/// </summary>
public class DatabaseStandardsProvider : IDatabaseStandardsProvider
{
    // Pre-configured standards for each supported database type
    private readonly Dictionary<string, DatabaseStandards> _standards = new()
    {
        [DatabaseTypes.PostgreSql] = new DatabaseStandards
        {
            DatabaseType = "PostgreSQL",
            NamingConvention = NamingConvention.SnakeCase,
            MaxIdentifierLength = 63,
            CaseSensitive = true,
            IdentifierQuoteStart = "\"",
            IdentifierQuoteEnd = "\"",
            SchemaSupport = "full",
            SupportsIdentity = true,
            IdentityStrategy = "sequence",
            ReservedKeywords = new[]
            {
                "ALL", "ANALYSE", "ANALYZE", "AND", "ANY", "ARRAY", "AS", "ASC", 
                "AUTHORIZATION", "BINARY", "BOTH", "CASE", "CAST", "CHECK", "COLLATE",
                "COLUMN", "CONSTRAINT", "CREATE", "CROSS", "CURRENT_DATE", "CURRENT_TIME",
                "CURRENT_TIMESTAMP", "CURRENT_USER", "DEFAULT", "DISTINCT", "DO", "ELSE",
                "END", "EXCEPT", "FALSE", "FETCH", "FOR", "FOREIGN", "FROM", "FULL",
                "GROUP", "HAVING", "IN", "INNER", "INTERSECT", "INTO", "IS", "JOIN",
                "LEFT", "LIKE", "LIMIT", "NATURAL", "NOT", "NULL", "OFFSET", "ON",
                "ONLY", "OR", "ORDER", "OUTER", "PRIMARY", "REFERENCES", "RIGHT",
                "SELECT", "TABLE", "THEN", "TO", "TRUE", "UNION", "UNIQUE", "USER",
                "USING", "WHEN", "WHERE", "WITH"
            }
        },
        
        [DatabaseTypes.MySql] = new DatabaseStandards
        {
            DatabaseType = "MySQL",
            NamingConvention = NamingConvention.SnakeCase,
            MaxIdentifierLength = 64,
            CaseSensitive = false,
            IdentifierQuoteStart = "`",
            IdentifierQuoteEnd = "`",
            SchemaSupport = "database",
            SupportsIdentity = true,
            IdentityStrategy = "auto_increment",
            ReservedKeywords = new[]
            {
                "ADD", "ALL", "ALTER", "AND", "AS", "ASC", "BETWEEN", "BY", "CASE",
                "CHAR", "CHECK", "COLUMN", "CONSTRAINT", "CREATE", "CROSS", "DATABASE",
                "DEFAULT", "DELETE", "DESC", "DISTINCT", "DROP", "ELSE", "EXISTS",
                "FOREIGN", "FROM", "FULL", "GROUP", "HAVING", "IN", "INDEX", "INNER",
                "INSERT", "INTEGER", "INTO", "IS", "JOIN", "KEY", "LEFT", "LIKE",
                "LIMIT", "NOT", "NULL", "ON", "OR", "ORDER", "OUTER", "PRIMARY",
                "REFERENCES", "RIGHT", "SELECT", "SET", "TABLE", "THEN", "TO",
                "UNION", "UNIQUE", "UPDATE", "USER", "USING", "VALUES", "WHEN",
                "WHERE", "WITH"
            }
        },
        
        [DatabaseTypes.Oracle] = new DatabaseStandards
        {
            DatabaseType = "Oracle",
            NamingConvention = NamingConvention.Uppercase,
            MaxIdentifierLength = 30, // 128 in Oracle 12.2+
            CaseSensitive = false,
            IdentifierQuoteStart = "\"",
            IdentifierQuoteEnd = "\"",
            SchemaSupport = "user",
            SupportsIdentity = true,
            IdentityStrategy = "sequence",
            ReservedKeywords = new[]
            {
                "ACCESS", "ADD", "ALL", "ALTER", "AND", "ANY", "AS", "ASC", "AUDIT",
                "BETWEEN", "BY", "CHAR", "CHECK", "CLUSTER", "COLUMN", "COMMENT",
                "COMPRESS", "CONNECT", "CREATE", "CURRENT", "DATE", "DECIMAL",
                "DEFAULT", "DELETE", "DESC", "DISTINCT", "DROP", "ELSE", "EXCLUSIVE",
                "EXISTS", "FILE", "FLOAT", "FOR", "FROM", "GRANT", "GROUP", "HAVING",
                "IDENTIFIED", "IN", "INCREMENT", "INDEX", "INSERT", "INTEGER",
                "INTERSECT", "INTO", "IS", "LEVEL", "LIKE", "LOCK", "LONG", "MINUS",
                "MODE", "NOT", "NULL", "NUMBER", "OF", "ON", "OPTION", "OR", "ORDER",
                "PRIOR", "PUBLIC", "RAW", "RENAME", "RESOURCE", "REVOKE", "ROW",
                "ROWID", "ROWNUM", "ROWS", "SELECT", "SESSION", "SET", "SHARE",
                "SIZE", "START", "SUCCESSFUL", "SYNONYM", "SYSDATE", "TABLE", "THEN",
                "TO", "TRIGGER", "UID", "UNION", "UNIQUE", "UPDATE", "USER",
                "VALIDATE", "VALUES", "VARCHAR", "VARCHAR2", "VIEW", "WHENEVER",
                "WHERE", "WITH"
            }
        },
        
        [DatabaseTypes.SqlServer] = new DatabaseStandards
        {
            DatabaseType = "SQL Server",
            NamingConvention = NamingConvention.PascalCase,
            MaxIdentifierLength = 128,
            CaseSensitive = false,
            IdentifierQuoteStart = "[",
            IdentifierQuoteEnd = "]",
            SchemaSupport = "full",
            SupportsIdentity = true,
            IdentityStrategy = "identity",
            ReservedKeywords = new[]
            {
                "ADD", "ALL", "ALTER", "AND", "ANY", "AS", "ASC", "AUTHORIZATION",
                "BACKUP", "BEGIN", "BETWEEN", "BREAK", "BROWSE", "BULK", "BY",
                "CASCADE", "CASE", "CHECK", "CHECKPOINT", "CLOSE", "CLUSTERED",
                "COALESCE", "COLUMN", "COMMIT", "CONSTRAINT", "CONTAINS", "CONTINUE",
                "CREATE", "CROSS", "CURRENT", "CURRENT_DATE", "CURRENT_TIME",
                "CURRENT_TIMESTAMP", "CURRENT_USER", "CURSOR", "DATABASE", "DECLARE",
                "DEFAULT", "DELETE", "DESC", "DISTINCT", "DOUBLE", "DROP", "ELSE",
                "END", "EXCEPT", "EXEC", "EXECUTE", "EXISTS", "EXIT", "FOREIGN",
                "FROM", "FULL", "FUNCTION", "GOTO", "GRANT", "GROUP", "HAVING",
                "IDENTITY", "IF", "IN", "INDEX", "INNER", "INSERT", "INTERSECT",
                "INTO", "IS", "JOIN", "KEY", "LEFT", "LIKE", "MERGE", "NOT", "NULL",
                "OF", "OFF", "ON", "OPEN", "OPTION", "OR", "ORDER", "OUTER", "OVER",
                "PRIMARY", "PRINT", "PROC", "PROCEDURE", "PUBLIC", "READ",
                "REFERENCES", "RETURN", "REVOKE", "RIGHT", "ROLLBACK", "RULE",
                "SCHEMA", "SELECT", "SET", "TABLE", "THEN", "TO", "TOP", "TRANSACTION",
                "TRIGGER", "TRUNCATE", "UNION", "UNIQUE", "UPDATE", "USER", "USING",
                "VALUES", "VIEW", "WHEN", "WHERE", "WHILE", "WITH"
            }
        }
    };

    /// <summary>
    /// Gets standards for the specified database type.
    /// Returns PostgreSQL defaults if the type is not recognized.
    /// </summary>
    public DatabaseStandards GetStandards(string databaseType)
    {
        var key = databaseType.ToLowerInvariant();
        return _standards.TryGetValue(key, out var standards)
            ? standards
            : _standards[DatabaseTypes.PostgreSql]; // Default to PostgreSQL
    }
}