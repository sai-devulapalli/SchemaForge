using System.Text;
using SchemaForge.Abstractions.Interfaces;
using SchemaForge.Abstractions.Models;

namespace SchemaForge.Services;

using System.Text.RegularExpressions;

/// <summary>
/// Converts SQL expressions between different database dialects.
/// Handles identifier quoting, function names, operators, and boolean literals.
/// Supports SQL Server, PostgreSQL, MySQL, and Oracle.
/// </summary>
public class SqlDialectConverter : ISqlDialectConverter
{
    // Dialect definitions for each supported database
    private readonly Dictionary<string, DatabaseDialect> _dialects;

    /// <summary>
    /// Initializes the converter with dialect definitions for all supported databases.
    /// </summary>
    public SqlDialectConverter()
    {
        _dialects = new Dictionary<string, DatabaseDialect>
        {
            [DatabaseTypes.SqlServer] = new DatabaseDialect
            {
                Name = "SQL Server",
                IdentifierQuoteStart = "[",
                IdentifierQuoteEnd = "]",
                CurrentDateFunction = "GETDATE()",
                CurrentTimestampFunction = "GETDATE()",
                NewGuidFunction = "NEWID()",
                NullCheckFunction = "ISNULL",
                StringConcatOperator = "+",
                LimitClause = "TOP",
                OffsetFetchClause = "OFFSET {0} ROWS FETCH NEXT {1} ROWS ONLY",
                BooleanTrue = "1",
                BooleanFalse = "0"
            },
            [DatabaseTypes.PostgreSql] = new DatabaseDialect
            {
                Name = "PostgreSQL",
                IdentifierQuoteStart = "\"",
                IdentifierQuoteEnd = "\"",
                CurrentDateFunction = "NOW()",
                CurrentTimestampFunction = "CURRENT_TIMESTAMP",
                NewGuidFunction = "gen_random_uuid()",
                NullCheckFunction = "COALESCE",
                StringConcatOperator = "||",
                LimitClause = "LIMIT",
                OffsetFetchClause = "LIMIT {1} OFFSET {0}",
                BooleanTrue = "TRUE",
                BooleanFalse = "FALSE"
            },
            [DatabaseTypes.MySql] = new DatabaseDialect
            {
                Name = "MySQL",
                IdentifierQuoteStart = "`",
                IdentifierQuoteEnd = "`",
                CurrentDateFunction = "NOW()",
                CurrentTimestampFunction = "CURRENT_TIMESTAMP",
                NewGuidFunction = "UUID()",
                NullCheckFunction = "IFNULL",
                StringConcatOperator = "CONCAT",
                LimitClause = "LIMIT",
                OffsetFetchClause = "LIMIT {1} OFFSET {0}",
                BooleanTrue = "1",
                BooleanFalse = "0"
            },
            [DatabaseTypes.Oracle] = new DatabaseDialect
            {
                Name = "Oracle",
                IdentifierQuoteStart = "\"",
                IdentifierQuoteEnd = "\"",
                CurrentDateFunction = "SYSDATE",
                CurrentTimestampFunction = "SYSTIMESTAMP",
                NewGuidFunction = "SYS_GUID()",
                NullCheckFunction = "NVL",
                StringConcatOperator = "||",
                LimitClause = "FETCH FIRST",
                OffsetFetchClause = "OFFSET {0} ROWS FETCH NEXT {1} ROWS ONLY",
                BooleanTrue = "1",
                BooleanFalse = "0"
            }
        };
    }

    /// <summary>
    /// Converts a complete view definition from source to target database dialect.
    /// Handles identifier quotes, functions, operators, and boolean literals.
    /// </summary>
    public string ConvertViewDefinition(string definition, string sourceDb, string targetDb,
        string? sourceSchema = null, string? targetSchema = null,
        Dictionary<string, string>? tableNameMap = null)
    {
        if (string.IsNullOrEmpty(definition)) return definition;

        var source = GetDialect(sourceDb);
        var target = GetDialect(targetDb);

        var converted = definition;

        // Remove CREATE VIEW header if present.
        // Handles: schema-qualified names, bracket-quoted, backtick-quoted, double-quoted.
        // e.g. "CREATE VIEW dbo.vw_Foo AS", "CREATE VIEW [dbo].[vw_Foo] AS", etc.
        converted = Regex.Replace(
            converted,
            @"CREATE\s+VIEW\s+(?:[\w\[\]""`.]+\s*\.\s*)?[\w\[\]""`.]+\s+AS\s+",
            "",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

        // Convert identifier quotes first (e.g. [brackets] → "double-quotes")
        converted = ConvertIdentifierQuotes(converted, source, target);

        // Replace schema-qualified table name references in ONE combined pass:
        // e.g. dbo.Employees, "dbo".Employees, "dbo"."Employees", dbo."Employees"
        // → "public"."employees"
        // Doing this in a single step avoids intermediate states where a half-converted
        // reference like "public".Employees gets merged into a single quoted identifier.
        if (!string.IsNullOrEmpty(sourceSchema) && !string.IsNullOrEmpty(targetSchema) && tableNameMap != null)
        {
            converted = ReplaceSchemaQualifiedTableNames(converted, sourceSchema, targetSchema, tableNameMap, target);
        }
        else if (!string.IsNullOrEmpty(sourceSchema) && !string.IsNullOrEmpty(targetSchema))
        {
            // No table map provided — just replace bare schema references.
            converted = ReplaceSchemaReferences(converted, sourceSchema, targetSchema, target);
        }

        // Replace any remaining unqualified table/column name references
        if (tableNameMap != null)
        {
            converted = ReplaceTableNames(converted, tableNameMap, target);
        }

        // Convert functions
        converted = ConvertFunctions(converted, source, target);

        // Convert operators
        converted = ConvertOperators(converted, source, target);

        // Convert boolean literals
        converted = ConvertBooleanLiterals(converted, source, target);

        return converted.Trim();
    }

    /// <summary>
    /// Converts a CHECK constraint expression between dialects.
    /// Removes CHECK keyword wrapper and converts syntax.
    /// </summary>
    public string ConvertCheckExpression(string expression, string sourceDb, string targetDb)
    {
        if (string.IsNullOrEmpty(expression)) return expression;

        var source = GetDialect(sourceDb);
        var target = GetDialect(targetDb);

        var converted = expression.Trim();

        // Remove CHECK keyword if present
        if (converted.StartsWith("CHECK", StringComparison.OrdinalIgnoreCase))
        {
            converted = converted.Substring(5).Trim();
        }
        // Strip matching outer wrapper parentheses only
        converted = StripOuterParentheses(converted);

        // Convert identifier quotes
        converted = ConvertIdentifierQuotes(converted, source, target);

        // Convert functions
        converted = ConvertFunctions(converted, source, target);

        // Convert boolean literals
        converted = ConvertBooleanLiterals(converted, source, target);

        return converted;
    }

    /// <summary>
    /// Converts a DEFAULT value expression between dialects.
    /// Handles function conversions like GETDATE() to NOW().
    /// </summary>
    public string ConvertDefaultExpression(string expression, string sourceDb, string targetDb)
    {
        if (string.IsNullOrEmpty(expression)) return expression;

        var source = GetDialect(sourceDb);
        var target = GetDialect(targetDb);

        var converted = StripOuterParentheses(expression);

        // Convert identifier quotes
        converted = ConvertIdentifierQuotes(converted, source, target);

        // Convert functions
        converted = ConvertFunctions(converted, source, target);

        return converted;
    }

    /// <summary>
    /// Converts a filtered index WHERE expression between dialects.
    /// </summary>
    public string ConvertFilterExpression(string expression, string sourceDb, string targetDb)
    {
        if (string.IsNullOrEmpty(expression)) return expression;

        var source = GetDialect(sourceDb);
        var target = GetDialect(targetDb);

        var converted = expression;

        // Convert identifier quotes
        converted = ConvertIdentifierQuotes(converted, source, target);

        // Convert functions
        converted = ConvertFunctions(converted, source, target);

        return converted;
    }

    /// <summary>
    /// Converts identifier quoting style (e.g., [name] to "name").
    /// </summary>
    private string ConvertIdentifierQuotes(string sql, DatabaseDialect source, DatabaseDialect target)
    {
        // Don't convert if same quote style
        if (source.IdentifierQuoteStart == target.IdentifierQuoteStart) return sql;

        // Escape regex special characters
        var escapeStart = Regex.Escape(source.IdentifierQuoteStart);
        var escapeEnd = Regex.Escape(source.IdentifierQuoteEnd);

        // Replace identifier quotes
        var pattern = $"{escapeStart}([^{escapeEnd}]+){escapeEnd}";
        
        return Regex.Replace(sql, pattern, match =>
        {
            var identifier = match.Groups[1].Value;
            return $"{target.IdentifierQuoteStart}{identifier}{target.IdentifierQuoteEnd}";
        });
    }

    /// <summary>
    /// Converts function names and syntax between dialects.
    /// </summary>
    private string ConvertFunctions(string sql, DatabaseDialect source, DatabaseDialect target)
    {
        var converted = sql;

        // Current date/time functions
        converted = ConvertFunction(converted, source.CurrentDateFunction, target.CurrentDateFunction);
        converted = ConvertFunction(converted, source.CurrentTimestampFunction, target.CurrentTimestampFunction);

        // GUID/UUID functions
        converted = ConvertFunction(converted, source.NewGuidFunction, target.NewGuidFunction);

        // NULL check functions (more complex - has parameters)
        converted = ConvertNullCheckFunction(converted, source.NullCheckFunction, target.NullCheckFunction);

        // String concatenation (special handling)
        converted = ConvertStringConcat(converted, source, target);

        return converted;
    }

    private string ConvertFunction(string sql, string sourceFunc, string targetFunc)
    {
        // Case-insensitive replacement
        return Regex.Replace(sql, Regex.Escape(sourceFunc), targetFunc, RegexOptions.IgnoreCase);
    }

    private string ConvertNullCheckFunction(string sql, string sourceFunc, string targetFunc)
    {
        // Convert ISNULL(a, b) <-> COALESCE(a, b) <-> IFNULL(a, b) <-> NVL(a, b)
        var pattern = $@"\b{sourceFunc}\s*\(";
        return Regex.Replace(sql, pattern, $"{targetFunc}(", RegexOptions.IgnoreCase);
    }

    private string ConvertStringConcat(string sql, DatabaseDialect source, DatabaseDialect target)
    {
        // This is complex and varies by database
        // MySQL uses CONCAT(), others use || or +
        
        if (source.StringConcatOperator == "CONCAT" && target.StringConcatOperator != "CONCAT")
        {
            // Convert CONCAT(a, b, c) to a || b || c or a + b + c
            var pattern = @"CONCAT\s*\(((?:[^()]|\((?:[^()]|\([^()]*\))*\))*)\)";
            return Regex.Replace(sql, pattern, match =>
            {
                var args = SplitFunctionArgs(match.Groups[1].Value);
                return string.Join($" {target.StringConcatOperator} ", args);
            }, RegexOptions.IgnoreCase);
        }
        else if (source.StringConcatOperator != "CONCAT" && target.StringConcatOperator == "CONCAT")
        {
            // Convert a || b || c or a + b + c to CONCAT(a, b, c)
            // This is more complex and context-dependent
            // For simplicity, we'll leave it as-is for now
        }

        return sql;
    }

    private string ConvertOperators(string sql, DatabaseDialect source, DatabaseDialect target)
    {
        // String concatenation operator conversion (simple cases)
        if (source.StringConcatOperator != target.StringConcatOperator && 
            source.StringConcatOperator != "CONCAT" && 
            target.StringConcatOperator != "CONCAT")
        {
            // Only convert simple operators (not CONCAT function)
            sql = sql.Replace(source.StringConcatOperator, target.StringConcatOperator);
        }

        return sql;
    }

    private string ConvertBooleanLiterals(string sql, DatabaseDialect source, DatabaseDialect target)
    {
        // Only convert boolean literals when they are in a clear boolean context
        // This prevents incorrectly converting numeric literals like "column = 1" for integer columns

        if (source.BooleanTrue != target.BooleanTrue)
        {
            // Only convert when preceded by comparison operators or boolean keywords
            // Pattern matches: = 1, != 1, <> 1, IS 1, NOT 1 (but not numeric contexts like "column + 1")
            sql = Regex.Replace(
                sql,
                $@"(=\s*|<>\s*|!=\s*|IS\s+|NOT\s+){Regex.Escape(source.BooleanTrue)}(?!\d)",
                $"$1{target.BooleanTrue}",
                RegexOptions.IgnoreCase);
        }

        if (source.BooleanFalse != target.BooleanFalse)
        {
            sql = Regex.Replace(
                sql,
                $@"(=\s*|<>\s*|!=\s*|IS\s+|NOT\s+){Regex.Escape(source.BooleanFalse)}(?!\d)",
                $"$1{target.BooleanFalse}",
                RegexOptions.IgnoreCase);
        }

        return sql;
    }

    /// <summary>
    /// Replaces schema-qualified table references in a single combined pass.
    /// Handles all quote/bracket combinations:
    ///   dbo.Table, [dbo].Table, "dbo".Table, dbo.[Table], dbo."Table", [dbo].[Table], "dbo"."Table"
    /// Doing this in one step prevents intermediate states where a half-replaced reference
    /// like "public".Table could be misread as a single double-quoted identifier.
    /// </summary>
    private static string ReplaceSchemaQualifiedTableNames(
        string sql,
        string sourceSchema,
        string targetSchema,
        Dictionary<string, string> tableNameMap,
        DatabaseDialect target)
    {
        var qs = target.IdentifierQuoteStart;
        var qe = target.IdentifierQuoteEnd;
        var escapedQs = Regex.Escape(qs);
        var escapedQe = Regex.Escape(qe);
        var escapedSchema = Regex.Escape(sourceSchema);
        var targetSchemaQuoted = $"{qs}{targetSchema}{qe}";

        // Schema part: matches "dbo", dbo, [dbo] (brackets already converted to quotes by this point)
        var schemaPattern = $@"(?:{escapedQs}{escapedSchema}{escapedQe}|{escapedSchema})";

        // Sort longest names first to avoid partial matches
        foreach (var (sourceName, targetName) in tableNameMap.OrderByDescending(kv => kv.Key.Length))
        {
            var escapedName = Regex.Escape(sourceName);
            var targetNameQuoted = $"{qs}{targetName}{qe}";
            var replacement = $"{targetSchemaQuoted}.{targetNameQuoted}";

            // Table part: matches "Employees", Employees (with word boundary on the unquoted form)
            var tablePattern = $@"(?:{escapedQs}{escapedName}{escapedQe}|(?<!\w){escapedName}(?!\w))";

            var combinedPattern = $@"{schemaPattern}\s*\.\s*{tablePattern}";

            sql = Regex.Replace(sql, combinedPattern, replacement, RegexOptions.IgnoreCase);
        }

        return sql;
    }

    /// <summary>
    /// Replaces source schema references in SQL with the target schema.
    /// Handles both quoted (e.g., "dbo"."Table") and unquoted (e.g., dbo.Table) references.
    /// </summary>
    private string ReplaceSchemaReferences(string sql, string sourceSchema, string targetSchema, DatabaseDialect target)
    {
        var qs = target.IdentifierQuoteStart;
        var qe = target.IdentifierQuoteEnd;
        var quotedTarget = $"{qs}{targetSchema}{qe}";

        // Replace quoted source schema references (already converted to target quote style)
        // e.g., "dbo"."Table" → "target_schema"."Table"
        var escapedQs = Regex.Escape(qs);
        var escapedQe = Regex.Escape(qe);
        var quotedPattern = $"{escapedQs}{Regex.Escape(sourceSchema)}{escapedQe}\\s*\\.";
        sql = Regex.Replace(sql, quotedPattern, $"{quotedTarget}.", RegexOptions.IgnoreCase);

        // Replace unquoted source schema references
        // e.g., dbo.Table → "target_schema".Table
        var unquotedPattern = $@"\b{Regex.Escape(sourceSchema)}\s*\.";
        sql = Regex.Replace(sql, unquotedPattern, $"{quotedTarget}.", RegexOptions.IgnoreCase);

        return sql;
    }

    /// <summary>
    /// Replaces source table names in SQL with their target equivalents.
    /// Handles both quoted and unquoted identifiers. Longer names are replaced first
    /// to avoid partial matches (e.g., "OrderDetails" before "Order").
    /// </summary>
    private static string ReplaceTableNames(string sql, Dictionary<string, string> tableNameMap, DatabaseDialect target)
    {
        var qs = target.IdentifierQuoteStart;
        var qe = target.IdentifierQuoteEnd;
        var escapedQs = Regex.Escape(qs);
        var escapedQe = Regex.Escape(qe);

        // Sort by source name length descending to avoid partial replacements
        foreach (var (sourceName, targetName) in tableNameMap.OrderByDescending(kv => kv.Key.Length))
        {
            if (sourceName == targetName) continue;

            var quotedTarget = $"{qs}{targetName}{qe}";

            // Replace quoted references: "Employees" → `employees`
            var quotedPattern = $"{escapedQs}{Regex.Escape(sourceName)}{escapedQe}";
            sql = Regex.Replace(sql, quotedPattern, quotedTarget, RegexOptions.IgnoreCase);

            // Replace unquoted references: Employees → `employees`
            // Use word boundary + negative lookbehind/lookahead for the target quote character
            // so we do NOT re-match identifiers that are already wrapped in target quotes.
            // e.g. after ReplaceSchemaQualifiedTableNames produced "employees" or `employees`,
            // the word boundary still fires because " and ` are non-word chars — without this
            // guard, "employees" would get double-wrapped to ""employees"" or ``employees``.
            var unquotedPattern = $@"(?<!{escapedQs})\b{Regex.Escape(sourceName)}\b(?!{escapedQe})";
            sql = Regex.Replace(sql, unquotedPattern, quotedTarget, RegexOptions.IgnoreCase);
        }

        return sql;
    }

    /// <summary>
    /// Strips matching outer wrapper parentheses from SQL expressions.
    /// Handles SQL Server's convention of wrapping defaults like (GETDATE()), ((0)), ('Pending').
    /// Only strips when outer parens are balanced wrappers, preserving function call syntax.
    /// </summary>
    private static string StripOuterParentheses(string expression)
    {
        var result = expression.Trim();
        while (result.Length >= 2 && result[0] == '(' && result[^1] == ')')
        {
            var inner = result[1..^1];
            int depth = 0;
            bool balanced = true;
            foreach (char c in inner)
            {
                if (c == '(') depth++;
                else if (c == ')')
                {
                    depth--;
                    if (depth < 0) { balanced = false; break; }
                }
            }
            if (balanced && depth == 0)
                result = inner.Trim();
            else
                break;
        }
        return result;
    }

    private DatabaseDialect GetDialect(string dbName)
    {
        var key = dbName?.ToLowerInvariant() ?? "";

        if (!_dialects.TryGetValue(key, out var dialect))
        {
            var supportedDatabases = string.Join(", ", _dialects.Keys);
            throw new ArgumentException(
                $"Unsupported database dialect: '{dbName}'. Supported dialects: {supportedDatabases}",
                nameof(dbName));
        }

        return dialect;
    }

    private List<string> SplitFunctionArgs(string args)
    {
        var result = new List<string>();
        var current = new StringBuilder();
        int depth = 0;

        foreach (char c in args)
        {
            if (c == ',' && depth == 0)
            {
                result.Add(current.ToString().Trim());
                current.Clear();
            }
            else
            {
                if (c == '(') depth++;
                if (c == ')') depth--;
                current.Append(c);
            }
        }

        if (current.Length > 0)
        {
            result.Add(current.ToString().Trim());
        }

        return result;
    }

    /// <summary>
    /// Detects the source database type based on SQL syntax patterns.
    /// Looks for database-specific functions and quote styles.
    /// </summary>
    public string DetectSourceDatabase(string sqlExpression)
    {
        if (string.IsNullOrEmpty(sqlExpression))
            return DatabaseTypes.SqlServer; // Default

        // SQL Server indicators
        if (sqlExpression.Contains("GETDATE()") || sqlExpression.Contains("["))
            return DatabaseTypes.SqlServer;

        // Oracle indicators
        if (sqlExpression.Contains("SYSDATE") || sqlExpression.Contains("NVL("))
            return DatabaseTypes.Oracle;

        // MySQL indicators
        if (sqlExpression.Contains("IFNULL(") || sqlExpression.Contains("`"))
            return DatabaseTypes.MySql;

        // PostgreSQL indicators
        if (sqlExpression.Contains("NOW()") || sqlExpression.Contains("COALESCE("))
            return DatabaseTypes.PostgreSql;

        return DatabaseTypes.SqlServer; // Default fallback
    }
}

