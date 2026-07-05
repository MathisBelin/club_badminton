using ClosedXML.Excel;
using BadmintonClub.Models;

namespace BadmintonClub.Services;

/// <summary>
/// Import de contacts depuis un fichier Excel (.xlsx). Réutilise la détection
/// d'en-têtes / le mapping de <see cref="CsvContactImporter"/>.
/// </summary>
public static class ExcelContactImporter
{
    public static List<Adherent> Parse(string path) => CsvContactImporter.FromRows(ReadRows(path));

    /// <summary>Lit toutes les lignes utilisées de la 1re feuille en tableaux de cellules.</summary>
    public static List<string[]> ReadRows(string path)
    {
        using var workbook = new XLWorkbook(path);
        var worksheet = workbook.Worksheets.First();

        var rows = new List<string[]>();
        var lastColumn = worksheet.LastColumnUsed()?.ColumnNumber() ?? 0;
        if (lastColumn == 0)
            return rows;

        foreach (var row in worksheet.RowsUsed())
        {
            var cells = new string[lastColumn];
            for (var c = 1; c <= lastColumn; c++)
                cells[c - 1] = row.Cell(c).GetString();
            rows.Add(cells);
        }

        return rows;
    }
}
