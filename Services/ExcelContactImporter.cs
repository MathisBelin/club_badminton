using ClosedXML.Excel;
using BadmintonClub.Models;

namespace BadmintonClub.Services;

/// <summary>
/// Import de contacts depuis un fichier Excel (.xlsx). Réutilise la détection
/// d'en-têtes / le mapping de <see cref="CsvContactImporter"/>.
/// </summary>
public static class ExcelContactImporter
{
    public static List<Adherent> Parse(string path)
    {
        using var workbook = new XLWorkbook(path);
        var worksheet = workbook.Worksheets.First();

        var lastColumn = worksheet.LastColumnUsed()?.ColumnNumber() ?? 0;
        if (lastColumn == 0)
            return new List<Adherent>();

        var rows = new List<string[]>();
        foreach (var row in worksheet.RowsUsed())
        {
            var cells = new string[lastColumn];
            for (var c = 1; c <= lastColumn; c++)
                cells[c - 1] = row.Cell(c).GetString();
            rows.Add(cells);
        }

        return CsvContactImporter.FromRows(rows);
    }
}
