using System.Globalization;
using System.Text;
using ClosedXML.Excel;

namespace ValuPro.Infrastructure.Import;

/// <summary>
/// Reads the first sheet of an .xlsx file, or a CSV file, into rows keyed by canonical column names.
/// Header names are matched loosely through an alias table ("GST Number", "gstin", "GST" → GSTIN).
/// </summary>
public static class TabularFile
{
    public const int MaxRows = 1000;

    public sealed record Sheet(List<(int Row, Dictionary<string, string> Values)> Rows, string? Error);

    public static Sheet Read(Stream file, string fileName, IReadOnlyDictionary<string, string> aliases, params string[] required)
    {
        List<List<string>> table;
        try
        {
            table = ReadTable(file, fileName);
        }
        catch (FormatException e) { return new([], e.Message); }
        catch (Exception) { return new([], "The file could not be read. Save it as .xlsx or .csv and try again."); }

        if (table.Count == 0) return new([], "The file is empty.");

        var header = table[0].Select(h => aliases.GetValueOrDefault(Normalize(h))).ToList();
        var missing = required.Where(r => !header.Contains(r)).ToList();
        if (missing.Count > 0)
            return new([], $"The first row must contain column names, including {string.Join(", ", missing)}. Download the template to see the layout.");

        var rows = new List<(int, Dictionary<string, string>)>();
        for (var i = 1; i < table.Count; i++)
        {
            var cells = table[i];
            if (cells.All(string.IsNullOrWhiteSpace)) continue;
            if (rows.Count == MaxRows) return new([], $"The file has more than {MaxRows} rows. Split it into smaller files.");

            var values = new Dictionary<string, string>();
            for (var c = 0; c < header.Count && c < cells.Count; c++)
                if (header[c] is { } column && !string.IsNullOrWhiteSpace(cells[c])) values[column] = cells[c].Trim();
            rows.Add((i + 1, values));
        }
        return new(rows, null);
    }

    /// <summary>Lower-case letters and digits only: "Mobile No." → "mobileno".</summary>
    public static string Normalize(string s) => new(s.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    /// <summary>Parses "₹ 1,15,000", "1,15,000.50" or "52000"; null when empty or not a number.</summary>
    public static decimal? Amount(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var clean = new string(s.Where(ch => char.IsDigit(ch) || ch is '.' or '-').ToArray());
        return decimal.TryParse(clean, NumberStyles.Number, CultureInfo.InvariantCulture, out var v) ? v : null;
    }

    /// <summary>Parses 2026-04-01, 01-04-2026, 01/04/2026 (day first, as in India) or an Excel date.</summary>
    public static DateOnly? Date(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        string[] formats = ["yyyy-MM-dd", "dd-MM-yyyy", "dd/MM/yyyy", "d/M/yyyy", "d-M-yyyy", "dd.MM.yyyy", "d MMM yyyy", "dd MMM yyyy", "dd/MM/yyyy HH:mm:ss", "M/d/yyyy h:mm:ss tt"];
        if (DateOnly.TryParseExact(s.Trim(), formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)) return d;
        if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var serial) && serial is > 20000 and < 80000)
            return DateOnly.FromDateTime(DateTime.FromOADate(serial));
        return null;
    }

    public static List<List<string>> ReadTable(Stream file, string fileName) =>
        Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".xlsx" => ReadExcel(file),
            ".csv" or ".txt" => ReadCsv(file),
            _ => throw new FormatException("Upload a .csv or .xlsx file."),
        };

    private static List<List<string>> ReadExcel(Stream file)
    {
        using var book = new XLWorkbook(file);
        var sheet = book.Worksheets.First();
        var used = sheet.RangeUsed();
        if (used is null) return [];
        var lastColumn = used.LastColumn().ColumnNumber();
        return
        [
            .. used.Rows().Select(r => Enumerable.Range(1, lastColumn).Select(c =>
            {
                var cell = r.Cell(c);
                // Dates as ISO, so day/month order is never guessed.
                return cell.DataType == XLDataType.DateTime ? cell.GetDateTime().ToString("yyyy-MM-dd") : cell.GetFormattedString().Trim();
            }).ToList()),
        ];
    }

    /// <summary>RFC 4180 CSV: quoted fields, doubled quotes, commas and line breaks inside quotes.</summary>
    private static List<List<string>> ReadCsv(Stream file)
    {
        using var reader = new StreamReader(file, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var text = reader.ReadToEnd();
        var rows = new List<List<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        var quoted = false;

        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (quoted)
            {
                if (ch == '"' && i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                else if (ch == '"') quoted = false;
                else field.Append(ch);
            }
            else if (ch == '"') quoted = true;
            else if (ch == ',') { row.Add(field.ToString()); field.Clear(); }
            else if (ch is '\n' or '\r')
            {
                if (ch == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                row.Add(field.ToString()); field.Clear();
                rows.Add(row); row = [];
            }
            else field.Append(ch);
        }
        if (field.Length > 0 || row.Count > 0) { row.Add(field.ToString()); rows.Add(row); }
        return rows;
    }
}
