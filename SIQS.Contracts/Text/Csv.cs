using System.Text;

namespace SIQS.Contracts.Text;

/// <summary>
/// Minimal RFC 4180-style CSV line parsing and writing. A field is quoted on write only when it
/// contains a comma, double quote, or newline; embedded quotes are doubled. Phase formats prefer
/// field shapes that avoid quoting, but the helper handles it safely when needed.
/// </summary>
public static class Csv
{
    private const char Delimiter = ',';
    private const char Quote = '"';

    /// <summary>Parses a single CSV line into its fields.</summary>
    public static IReadOnlyList<string> ParseLine(string line)
    {
        if (!line.Contains(Quote))
        {
            return line.Split(Delimiter);
        }

        var fields = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (inQuotes)
            {
                if (c == Quote)
                {
                    if (i + 1 < line.Length && line[i + 1] == Quote)
                    {
                        field.Append(Quote);
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    field.Append(c);
                }
            }
            else if (c == Quote)
            {
                inQuotes = true;
            }
            else if (c == Delimiter)
            {
                fields.Add(field.ToString());
                field.Clear();
            }
            else
            {
                field.Append(c);
            }
        }

        fields.Add(field.ToString());
        return fields;
    }

    /// <summary>Writes fields as a single CSV line, quoting only where required.</summary>
    public static string WriteLine(IEnumerable<string> fields)
        => string.Join(Delimiter, fields.Select(Escape));

    private static string Escape(string field)
    {
        var needsQuoting = field.Contains(Delimiter) || field.Contains(Quote)
            || field.Contains('\n') || field.Contains('\r');

        if (!needsQuoting)
        {
            return field;
        }

        return string.Concat(Quote, field.Replace("\"", "\"\""), Quote);
    }
}
