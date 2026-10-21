using System.Globalization;
using System.Text;

namespace AssetIQ.Api.Infrastructure;

/// <summary>
/// RFC 4180 CSV for the report downloads: comma-separated, CRLF line endings, fields quoted when they
/// contain a comma, quote or line break, and UTF-8 with a byte-order mark so Excel reads names such as
/// "Chikore — BAK" correctly.
/// </summary>
public sealed class CsvBuilder
{
    private static readonly byte[] Utf8Bom = [0xEF, 0xBB, 0xBF];
    private readonly StringBuilder _text = new();

    public CsvBuilder Row(params object?[] cells)
    {
        for (var i = 0; i < cells.Length; i++)
        {
            if (i > 0) _text.Append(',');
            _text.Append(Cell(cells[i]));
        }
        _text.Append("\r\n");
        return this;
    }

    public byte[] ToBytes() => [.. Utf8Bom, .. Encoding.UTF8.GetBytes(_text.ToString())];

    public override string ToString() => _text.ToString();

    internal static string Cell(object? value) => value switch
    {
        null => "",
        string text => Quote(NeutraliseFormula(text)),
        bool flag => flag ? "Yes" : "No",
        IFormattable number => Quote(number.ToString(null, CultureInfo.InvariantCulture)),
        _ => Quote(NeutraliseFormula(value.ToString() ?? "")),
    };

    /// <summary>
    /// Spreadsheets run a cell that starts with = + - @ (or a tab / carriage return) as a formula, so free
    /// text typed into AssetIQ (names, notes, reasons) is prefixed with an apostrophe: shown as text, never executed.
    /// </summary>
    private static string NeutraliseFormula(string text) =>
        text.Length > 0 && text[0] is '=' or '+' or '-' or '@' or '\t' or '\r' ? "'" + text : text;

    private static string Quote(string text) =>
        text.AsSpan().IndexOfAny(",\"\r\n") >= 0 ? $"\"{text.Replace("\"", "\"\"")}\"" : text;
}
