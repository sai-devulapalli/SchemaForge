using SchemaForge.Services.Interfaces;
using System.Text;
using Microsoft.Extensions.Options;
using SchemaForge.Configuration;

namespace SchemaForge.Services;

/// <summary>
/// Converts identifier names to the appropriate naming convention for the target database.
/// Supports snake_case, PascalCase, camelCase, lowercase, UPPERCASE, and preserve modes.
/// Also handles identifier length limits and reserved keyword quoting.
/// </summary>
public class SnakeCaseConverter(
    IOptions<MigrationSettings> settings,
    IDatabaseStandardsProvider standardsProvider) : INamingConverter
{
    private readonly MigrationSettings _settings = settings.Value;

    /// <summary>
    /// Converts an identifier to the target naming convention.
    /// Applies configured rules and handles length limits and reserved keywords.
    /// </summary>
    public string Convert(string input)
    {
        if (string.IsNullOrEmpty(input)) return input;

        var standards = standardsProvider.GetStandards(_settings.TargetDatabaseType);

        string result;

        if (_settings.PreserveSourceCase)
        {
            result = input;
        }
        else if (_settings.NamingConvention == "preserve")
        {
            result = input;
        }
        else if (_settings.NamingConvention != "auto")
        {
            result = ApplyNamingConvention(input, _settings.NamingConvention);
        }
        else if (_settings.UseTargetDatabaseStandards)
        {
            result = standards.NamingConvention switch
            {
                NamingConvention.SnakeCase => ToSnakeCase(input),
                NamingConvention.PascalCase => ToPascalCase(input),
                NamingConvention.Lowercase => input.ToLowerInvariant().Replace("_", ""),
                NamingConvention.Uppercase => input.ToUpperInvariant(),
                NamingConvention.Preserve => input,
                _ => ToSnakeCase(input)
            };
        }
        else
        {
            result = ToSnakeCase(input);
        }

        // Truncate if exceeds max length
        var maxLength = _settings.MaxIdentifierLength > 0
            ? _settings.MaxIdentifierLength
            : standards.MaxIdentifierLength;

        if (result.Length > maxLength && maxLength > 0)
        {
            result = result[..maxLength];
        }

        // Quote if reserved keyword
        if (standards.ReservedKeywords.Contains(result.ToUpperInvariant()))
        {
            result = $"{standards.IdentifierQuoteStart}{result}{standards.IdentifierQuoteEnd}";
        }

        return result;
    }

    /// <summary>
    /// Applies a specific naming convention to the input string.
    /// </summary>
    private static string ApplyNamingConvention(string input, string convention)
    {
        return convention.ToLowerInvariant() switch
        {
            "snake_case" => ToSnakeCase(input),
            "camelcase" => ToCamelCase(input),
            "pascalcase" => ToPascalCase(input),
            "lowercase" => input.ToLowerInvariant().Replace("_", ""),
            "uppercase" => input.ToUpperInvariant(),
            _ => ToSnakeCase(input)
        };
    }

    /// <summary>
    /// Converts PascalCase or camelCase to snake_case.
    /// Handles consecutive uppercase letters correctly (e.g., "XMLParser" -> "xml_parser").
    /// </summary>
    private static string ToSnakeCase(string input)
    {
        if (string.IsNullOrEmpty(input)) return input;

        var sb = new StringBuilder();

        for (int i = 0; i < input.Length; i++)
        {
            char c = input[i];

            if (c == '_')
            {
                sb.Append('_');
                continue;
            }

            if (char.IsUpper(c) && i > 0 && input[i - 1] != '_')
            {
                char prev = input[i - 1];
                bool nextIsLower = i < input.Length - 1 && char.IsLower(input[i + 1]);

                if (char.IsLower(prev) || (nextIsLower && char.IsUpper(prev)))
                {
                    sb.Append('_');
                }
            }

            sb.Append(char.ToLowerInvariant(c));
        }

        return sb.ToString();
    }

    /// <summary>
    /// Converts snake_case to PascalCase.
    /// </summary>
    private static string ToPascalCase(string input)
    {
        if (string.IsNullOrEmpty(input)) return input;

        var parts = input.Split('_', StringSplitOptions.RemoveEmptyEntries);
        var sb = new StringBuilder();

        foreach (var part in parts)
        {
            if (part.Length > 0)
            {
                sb.Append(char.ToUpperInvariant(part[0]));
                if (part.Length > 1)
                {
                    sb.Append(part[1..].ToLowerInvariant());
                }
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Converts snake_case to camelCase.
    /// </summary>
    private static string ToCamelCase(string input)
    {
        var pascal = ToPascalCase(input);
        if (pascal.Length > 0)
        {
            return string.Concat(char.ToLowerInvariant(pascal[0]).ToString(), pascal[1..]);
        }
        return pascal;
    }
}
