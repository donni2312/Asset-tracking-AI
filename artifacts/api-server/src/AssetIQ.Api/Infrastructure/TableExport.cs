using System.Globalization;
using ClosedXML.Excel;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace AssetIQ.Api.Infrastructure;

/// <summary>A column of an exported table; money columns are right-aligned with two decimals.</summary>
public sealed record ExportColumn(string Header, bool Money = false, bool Number = false);

/// <summary>A row of an exported table. Subtotal and total rows are bold (AU-09: exports keep totals and headers).</summary>
public sealed record ExportRow(IReadOnlyList<object?> Cells, bool Bold = false);

/// <summary>A report as both exports see it: title, the filters it was run with, and the table.</summary>
public sealed record ExportTable(string Title, IReadOnlyList<string> Subtitle, IReadOnlyList<ExportColumn> Columns, IReadOnlyList<ExportRow> Rows);

/// <summary>Excel (ClosedXML) and PDF (QuestPDF) renderings of an <see cref="ExportTable"/>.</summary>
public static class TableExport
{
    public const string ExcelContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    public const string PdfContentType = "application/pdf";

    public static byte[] Excel(ExportTable table)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add(SheetName(table.Title));
        var row = 1;
        sheet.Cell(row, 1).Value = table.Title;
        sheet.Cell(row, 1).Style.Font.Bold = true;
        sheet.Cell(row, 1).Style.Font.FontSize = 14;
        foreach (var line in table.Subtitle) sheet.Cell(++row, 1).Value = line;
        row += 2;
        var headerRow = row;
        for (var c = 0; c < table.Columns.Count; c++)
        {
            var cell = sheet.Cell(row, c + 1);
            cell.Value = table.Columns[c].Header;
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#DCE3EE");
        }
        foreach (var r in table.Rows)
        {
            row++;
            for (var c = 0; c < table.Columns.Count; c++)
            {
                var cell = sheet.Cell(row, c + 1);
                var value = c < r.Cells.Count ? r.Cells[c] : null;
                switch (value)
                {
                    case null: break;
                    case decimal d: cell.Value = d; break;
                    case int i: cell.Value = i; break;
                    case bool b: cell.Value = b ? "Yes" : "No"; break;
                    // Text that a spreadsheet would run as a formula is stored as plain text.
                    default: cell.Value = value.ToString(); cell.Style.NumberFormat.Format = "@"; break;
                }
                if (table.Columns[c].Money) cell.Style.NumberFormat.Format = "#,##0.00";
                if (r.Bold) cell.Style.Font.Bold = true;
            }
        }
        sheet.SheetView.FreezeRows(headerRow);
        sheet.Columns(1, table.Columns.Count).AdjustToContents(headerRow, row, 10, 60);
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    public static byte[] Pdf(ExportTable table) => Document.Create(document => document.Page(page =>
    {
        page.Size(table.Columns.Count > 7 ? PageSizes.A4.Landscape() : PageSizes.A4);
        page.Margin(28);
        page.DefaultTextStyle(t => t.FontSize(8));
        page.Header().Column(col =>
        {
            col.Item().Text(table.Title).FontSize(14).Bold();
            foreach (var line in table.Subtitle) col.Item().Text(line).FontColor(Colors.Grey.Darken2);
            col.Item().PaddingBottom(6);
        });
        page.Content().Table(t =>
        {
            t.ColumnsDefinition(cols =>
            {
                for (var c = 0; c < table.Columns.Count; c++)
                {
                    if (c == 0 || (!table.Columns[c].Money && !table.Columns[c].Number)) cols.RelativeColumn(2);
                    else cols.RelativeColumn();
                }
            });
            t.Header(h =>
            {
                foreach (var column in table.Columns)
                {
                    var cell = h.Cell().Background(Colors.Grey.Lighten2).Padding(3);
                    if (column.Money || column.Number) cell.AlignRight().Text(column.Header).Bold();
                    else cell.Text(column.Header).Bold();
                }
            });
            foreach (var r in table.Rows)
            {
                for (var c = 0; c < table.Columns.Count; c++)
                {
                    var value = c < r.Cells.Count ? r.Cells[c] : null;
                    var cell = t.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(3);
                    var text = Format(value, table.Columns[c].Money);
                    var aligned = table.Columns[c].Money || table.Columns[c].Number ? cell.AlignRight() : cell;
                    if (r.Bold) aligned.Text(text).Bold(); else aligned.Text(text);
                }
            }
        });
        page.Footer().AlignRight().Text(x =>
        {
            x.Span("Page ");
            x.CurrentPageNumber();
            x.Span(" of ");
            x.TotalPages();
        });
    })).GeneratePdf();

    public static string Format(object? value, bool money = false) => value switch
    {
        null => "",
        decimal d when money => d.ToString("#,##0.00", CultureInfo.InvariantCulture),
        decimal d => d.ToString("0.##", CultureInfo.InvariantCulture),
        bool b => b ? "Yes" : "No",
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? "",
    };

    private static string SheetName(string title)
    {
        var clean = new string(title.Where(c => !"[]:*?/\\".Contains(c)).ToArray());
        return clean.Length <= 31 ? clean : clean[..31];
    }
}
