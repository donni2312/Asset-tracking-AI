using System.Globalization;
using System.Text;
using ClosedXML.Excel;

namespace AssetIQ.Api.Infrastructure;

/// <summary>One data row of an uploaded sheet: its row number in the file and its cells by (lower-case) column header.</summary>
public sealed record SheetRow(int RowNumber, IReadOnlyDictionary<string, string?> Cells)
{
    public string? this[string column] => Cells.TryGetValue(column, out var v) && !string.IsNullOrWhiteSpace(v) ? v.Trim() : null;
}

/// <summary>
/// Phase 4d (SF-10): reads an uploaded CSV or Excel sheet into rows, and writes the import template. The first row
/// holds the column headers (case and spaces are ignored: "Serial Number" = "serial_number"); empty rows are skipped.
/// </summary>
public static class SpreadsheetRows
{
    public const int MaxRows = 5000;

    public static List<SheetRow> Read(Stream content, string fileName)
    {
        var rows = fileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase) ? ReadExcel(content) : ReadCsv(content);
        if (rows.Count > MaxRows) throw new ApiException(StatusCodes.Status400BadRequest, $"file: at most {MaxRows} rows per import");
        return rows;
    }

    public static string Key(string header) => string.Join("_", header.Trim().ToLowerInvariant().Split([' ', '-', '/'], StringSplitOptions.RemoveEmptyEntries));

    public static byte[] TemplateCsv(IReadOnlyList<string> columns, IReadOnlyList<string> example) =>
        Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(string.Join(",", columns.Select(Quote)) + "\r\n" + string.Join(",", example.Select(Quote)) + "\r\n")).ToArray();

    public static byte[] TemplateExcel(IReadOnlyList<string> columns, IReadOnlyList<string> example, IReadOnlyList<(string Column, string Meaning)> notes)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Assets");
        for (var c = 0; c < columns.Count; c++)
        {
            sheet.Cell(1, c + 1).Value = columns[c];
            sheet.Cell(1, c + 1).Style.Font.Bold = true;
            sheet.Cell(2, c + 1).Value = example[c];
        }
        sheet.Columns().AdjustToContents();
        var help = workbook.Worksheets.Add("Columns");
        help.Cell(1, 1).Value = "Column";
        help.Cell(1, 2).Value = "What to enter";
        help.Row(1).Style.Font.Bold = true;
        for (var i = 0; i < notes.Count; i++)
        {
            help.Cell(i + 2, 1).Value = notes[i].Column;
            help.Cell(i + 2, 2).Value = notes[i].Meaning;
        }
        help.Columns().AdjustToContents();
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static List<SheetRow> ReadExcel(Stream content)
    {
        XLWorkbook workbook;
        try { workbook = new XLWorkbook(content); }
        catch (Exception ex) when (ex is not OperationCanceledException) { throw new ApiException(StatusCodes.Status400BadRequest, "file: not a readable Excel workbook"); }
        using (workbook)
        {
            var sheet = workbook.Worksheets.First();
            var used = sheet.RangeUsed();
            if (used is null) return [];
            var lastColumn = used.LastColumn().ColumnNumber();
            var headers = Enumerable.Range(1, lastColumn).Select(c => Key(sheet.Cell(1, c).GetString())).ToList();
            var rows = new List<SheetRow>();
            for (var r = 2; r <= used.LastRow().RowNumber(); r++)
            {
                var cells = new Dictionary<string, string?>();
                for (var c = 1; c <= lastColumn; c++)
                {
                    if (headers[c - 1].Length == 0) continue;
                    var cell = sheet.Cell(r, c);
                    cells[headers[c - 1]] = cell.DataType == XLDataType.DateTime
                        ? cell.GetDateTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                        : cell.DataType == XLDataType.Number ? cell.GetDouble().ToString(CultureInfo.InvariantCulture) : cell.GetString();
                }
                if (cells.Values.All(string.IsNullOrWhiteSpace)) continue;
                rows.Add(new SheetRow(r, cells));
            }
            return rows;
        }
    }

    private static List<SheetRow> ReadCsv(Stream content)
    {
        using var reader = new StreamReader(content, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var records = ParseCsv(reader.ReadToEnd());
        if (records.Count == 0) return [];
        var headers = records[0].Select(Key).ToList();
        var rows = new List<SheetRow>();
        for (var i = 1; i < records.Count; i++)
        {
            var cells = new Dictionary<string, string?>();
            for (var c = 0; c < headers.Count; c++)
                if (headers[c].Length > 0) cells[headers[c]] = c < records[i].Count ? records[i][c] : null;
            if (cells.Values.All(string.IsNullOrWhiteSpace)) continue;
            rows.Add(new SheetRow(i + 1, cells));
        }
        return rows;
    }

    /// <summary>RFC 4180: commas, quoted fields with doubled quotes, CRLF or LF.</summary>
    private static List<List<string>> ParseCsv(string text)
    {
        var records = new List<List<string>>();
        var record = new List<string>();
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
            else if (ch == ',') { record.Add(field.ToString()); field.Clear(); }
            else if (ch is '\r' or '\n')
            {
                if (ch == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                record.Add(field.ToString());
                field.Clear();
                records.Add(record);
                record = [];
            }
            else field.Append(ch);
        }
        if (field.Length > 0 || record.Count > 0)
        {
            record.Add(field.ToString());
            records.Add(record);
        }
        return records;
    }

    private static string Quote(string value) => value.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? $"\"{value.Replace("\"", "\"\"")}\"" : value;
}
