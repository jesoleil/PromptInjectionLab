using System.Globalization;
using System.Reflection;
using System.Text;

namespace PromptInjectionLab.Core;

/// <summary>
/// Minimal, dependency-free CSV reader/writer (RFC 4180 style quoting).
/// Used instead of a third-party CSV package so the project builds with no
/// external NuGet dependencies. Handles commas, quotes and embedded newlines.
/// Records map to/from public instance properties by header name (case-insensitive).
/// </summary>
public static class Csv
{
    // ---- Writing ----

    public static string[] Columns<T>() =>
        typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance)
                 .Where(p => p.CanRead)
                 .Select(p => p.Name)
                 .ToArray();

    public static string HeaderLine<T>() => FormatRow(Columns<T>());

    public static string FormatRecord<T>(T record)
    {
        var props = typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.CanRead);
        return FormatRow(props.Select(p => FormatValue(p.GetValue(record))));
    }

    public static string FormatRow(IEnumerable<string> fields) =>
        string.Join(",", fields.Select(Escape));

    private static string FormatValue(object? v) => v switch
    {
        null => "",
        bool b => b ? "True" : "False",
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => v.ToString() ?? "",
    };

    private static string Escape(string field)
    {
        field ??= "";
        if (field.Contains('"') || field.Contains(',') || field.Contains('\n') || field.Contains('\r'))
            return "\"" + field.Replace("\"", "\"\"") + "\"";
        return field;
    }

    // ---- Reading ----

    public static List<T> ReadRecords<T>(string text) where T : new()
    {
        var rows = ParseRows(text);
        var result = new List<T>();
        if (rows.Count == 0) return result;

        var header = rows[0];
        var props = typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance)
                             .Where(p => p.CanWrite)
                             .ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);

        for (int r = 1; r < rows.Count; r++)
        {
            var row = rows[r];
            if (row.Length == 1 && row[0].Length == 0) continue; // skip blank line
            var item = new T();
            for (int c = 0; c < header.Length && c < row.Length; c++)
            {
                if (!props.TryGetValue(header[c], out var prop)) continue;
                var converted = ConvertTo(row[c], prop.PropertyType);
                prop.SetValue(item, converted);
            }
            result.Add(item);
        }
        return result;
    }

    private static object? ConvertTo(string s, Type target)
    {
        var underlying = Nullable.GetUnderlyingType(target);
        if (underlying is not null)
        {
            if (string.IsNullOrEmpty(s)) return null;
            return ConvertTo(s, underlying);
        }
        if (target == typeof(string)) return s;
        if (string.IsNullOrEmpty(s))
            return target.IsValueType ? Activator.CreateInstance(target) : null;
        if (target == typeof(int)) return int.Parse(s, CultureInfo.InvariantCulture);
        if (target == typeof(long)) return long.Parse(s, CultureInfo.InvariantCulture);
        if (target == typeof(double)) return double.Parse(s, CultureInfo.InvariantCulture);
        if (target == typeof(bool)) return bool.Parse(s);
        return Convert.ChangeType(s, target, CultureInfo.InvariantCulture);
    }

    /// <summary>Parses CSV text into rows of fields, honoring quoted fields and embedded newlines.</summary>
    public static List<string[]> ParseRows(string text)
    {
        var rows = new List<string[]>();
        var fields = new List<string>();
        var sb = new StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < text.Length; i++)
        {
            char ch = text[i];
            if (inQuotes)
            {
                if (ch == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"') { sb.Append('"'); i++; }
                    else inQuotes = false;
                }
                else sb.Append(ch);
            }
            else
            {
                switch (ch)
                {
                    case '"': inQuotes = true; break;
                    case ',': fields.Add(sb.ToString()); sb.Clear(); break;
                    case '\r': break; // handled with \n
                    case '\n':
                        fields.Add(sb.ToString()); sb.Clear();
                        rows.Add(fields.ToArray()); fields.Clear();
                        break;
                    default: sb.Append(ch); break;
                }
            }
        }
        // trailing field/row (file not ending in newline)
        if (sb.Length > 0 || fields.Count > 0)
        {
            fields.Add(sb.ToString());
            rows.Add(fields.ToArray());
        }
        return rows;
    }
}
