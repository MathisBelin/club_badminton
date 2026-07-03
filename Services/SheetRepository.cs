using System.IO;
using System.Text.Json;
using BadmintonClub.Models;

namespace BadmintonClub.Services;

/// <summary>
/// Registre local (JSON) des Google Sheets créés par l'application.
/// </summary>
public class SheetRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _path;

    public SheetRepository(string path) => _path = path;

    public string Path => _path;

    public List<SheetRecord> Load()
    {
        if (!File.Exists(_path))
            return new List<SheetRecord>();

        try
        {
            var json = File.ReadAllText(_path);
            if (string.IsNullOrWhiteSpace(json))
                return new List<SheetRecord>();

            return JsonSerializer.Deserialize<List<SheetRecord>>(json, JsonOptions)
                   ?? new List<SheetRecord>();
        }
        catch (JsonException)
        {
            return new List<SheetRecord>();
        }
    }

    public void Save(IEnumerable<SheetRecord> sheets)
    {
        var dir = System.IO.Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        File.WriteAllText(_path, JsonSerializer.Serialize(sheets, JsonOptions));
    }

    /// <summary>Ajoute un enregistrement et sauvegarde.</summary>
    public void Add(SheetRecord record)
    {
        var list = Load();
        list.Add(record);
        Save(list);
    }
}
